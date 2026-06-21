from __future__ import annotations

import json
import msvcrt
import os
import re
import sys
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError
from playwright.sync_api import sync_playwright


APP_DIR = (
    Path(sys.executable).resolve().parent
    if getattr(sys, "frozen", False)
    else Path(__file__).resolve().parent
)
BUNDLED_BROWSERS_DIR = APP_DIR / "playwright-browsers"
if BUNDLED_BROWSERS_DIR.is_dir():
    os.environ.setdefault("PLAYWRIGHT_BROWSERS_PATH", str(BUNDLED_BROWSERS_DIR))

RUNTIME_DIR = Path(os.environ.get("LOCALAPPDATA", str(APP_DIR))) / "CodexUsageMonitor"
RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
runtime_profile = RUNTIME_DIR / "browser-profile"

DATA_PATH = RUNTIME_DIR / "codex-usage.json"
LOG_PATH = RUNTIME_DIR / "codex-usage-scraper.log"
PROFILE_DIR = runtime_profile
LOCK_PATH = RUNTIME_DIR / "codex-usage-scraper.lock"
PID_PATH = RUNTIME_DIR / "codex-usage-scraper.pid"
PAGE_TEXT_PATH = RUNTIME_DIR / "codex-usage-page-text.txt"
SETTINGS_PATH = RUNTIME_DIR / "codex-usage-settings.json"
DEBUG_CAPTURE = os.environ.get("CODEX_USAGE_DEBUG_CAPTURE") == "1"
USAGE_URL = "https://chatgpt.com/codex/cloud/settings/analytics#usage"
DEFAULT_INTERVAL_SECONDS = 10 * 60


def log(message: str) -> None:
    line = f"{datetime.now():%Y-%m-%d %H:%M:%S} {message}"
    with LOG_PATH.open("a", encoding="utf-8") as handle:
        handle.write(line + "\n")


def mask_page_text(text: str) -> str:
    masked_lines: list[str] = []
    account_terms = re.compile(r"(account|profile|email|user name|계정|프로필|이메일|사용자 이름)", re.I)
    email_pattern = re.compile(r"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", re.I)
    for line in text.splitlines():
        if account_terms.search(line):
            masked_lines.append("[REDACTED_ACCOUNT_LINE]")
            continue
        line = email_pattern.sub("[REDACTED_EMAIL]", line)
        line = re.sub(r"\d", "#", line)
        masked_lines.append(line)
    return "\n".join(masked_lines[:500])


def write_debug_capture(text: str) -> None:
    if DEBUG_CAPTURE:
        PAGE_TEXT_PATH.write_text(mask_page_text(text), encoding="utf-8")


def read_existing_data() -> dict[str, Any]:
    if not DATA_PATH.exists():
        return {}
    try:
        data = json.loads(DATA_PATH.read_text(encoding="utf-8-sig"))
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def get_interval_seconds() -> int:
    if not SETTINGS_PATH.exists():
        return DEFAULT_INTERVAL_SECONDS
    try:
        settings = json.loads(SETTINGS_PATH.read_text(encoding="utf-8-sig"))
        if not isinstance(settings, dict):
            return DEFAULT_INTERVAL_SECONDS
        interval = int(settings.get("refreshIntervalSeconds", DEFAULT_INTERVAL_SECONDS))
    except Exception:
        return DEFAULT_INTERVAL_SECONDS

    allowed = {600, 900, 1800, 3600}
    if interval not in allowed:
        return DEFAULT_INTERVAL_SECONDS
    return interval


def write_usage(data: dict[str, Any]) -> None:
    temp_path = DATA_PATH.with_suffix(".json.tmp")
    temp_path.write_text(
        json.dumps(data, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    temp_path.replace(DATA_PATH)


def acquire_lock():
    lock_handle = LOCK_PATH.open("a+", encoding="utf-8")
    try:
        msvcrt.locking(lock_handle.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        log("Another scraper is already running. Exiting this instance.")
        lock_handle.close()
        return None
    return lock_handle


def line_list(text: str) -> list[str]:
    return [line.strip() for line in text.splitlines() if line.strip()]


def find_section_value(lines: list[str], labels: list[str]) -> tuple[int | None, str | None]:
    lowered = [line.lower() for line in lines]
    for index, line in enumerate(lowered):
        if any(label.lower() in line for label in labels):
            window = lines[index : index + 5]
            percent = None
            reset = None
            for item in window:
                match = re.search(r"(\d{1,3})\s*%\s*(?:남음|remaining|left)?", item, re.I)
                if match and percent is None:
                    percent = max(0, min(100, int(match.group(1))))
                if reset is None and ("초기화" in item or re.search(r"\b(reset|resets)\b", item, re.I)):
                    reset = item
            if percent is not None:
                return percent, reset
    return None, None


def find_credit_value(lines: list[str]) -> float | None:
    for index, line in enumerate(lines):
        if "남은 크레딧" in line or re.search(r"\b(remaining|available)\s+credits?\b", line, re.I):
            for item in lines[index + 1 : index + 8]:
                match = re.search(r"([-+]?\d[\d,]*(?:\.\d+)?)", item)
                if match:
                    return float(match.group(1).replace(",", ""))

    lowered = [line.lower() for line in lines]
    for index, line in enumerate(lowered):
        if "크레딧" in line or "credit" in line:
            window = lines[index : index + 8]
            for item in window:
                if "%" in item:
                    continue
                match = re.search(r"([-+]?\d[\d,]*(?:\.\d+)?)", item)
                if match:
                    return float(match.group(1).replace(",", ""))
    return None


def parse_usage(text: str) -> dict[str, Any]:
    lines = line_list(text)
    five_percent, five_reset = find_section_value(
        lines,
        ["5시간 사용 한도", "5시간 한도", "5-hour", "5 hour", "5h"],
    )
    weekly_percent, weekly_reset = find_section_value(
        lines,
        ["주간 사용 한도", "주간 한도", "weekly usage limit", "weekly limit", "week"],
    )
    credits_remaining = find_credit_value(lines)

    if five_percent is None or weekly_percent is None:
        matches = re.findall(r"(\d{1,3})\s*%\s*(?:남음|remaining|left)", text, re.I)
        percents = [max(0, min(100, int(value))) for value in matches]
        if five_percent is None and len(percents) >= 1:
            five_percent = percents[0]
        if weekly_percent is None and len(percents) >= 2:
            weekly_percent = percents[1]

    if five_percent is None or weekly_percent is None:
        raise ValueError("Could not find both 5-hour and weekly usage percentages.")

    existing = read_existing_data()
    five_reset_at = parse_reset_time(five_reset, weekly=False)
    weekly_reset_at = parse_reset_time(weekly_reset, weekly=True)

    return {
        "fiveHourRemaining": five_percent,
        "fiveHourReset": five_reset or existing.get("fiveHourReset") or "",
        "fiveHourResetAt": five_reset_at or existing.get("fiveHourResetAt") or "",
        "weeklyRemaining": weekly_percent,
        "weeklyReset": weekly_reset or existing.get("weeklyReset") or "",
        "weeklyResetAt": weekly_reset_at or existing.get("weeklyResetAt") or "",
        "creditsRemaining": credits_remaining if credits_remaining is not None else existing.get("creditsRemaining", 0),
        "updatedAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "source": USAGE_URL,
        "status": "ok",
    }


def collect_credits_from_balance_tab(page) -> float | None:
    click_targets = [
        ("text", "잔고"),
        ("text", "Balance"),
        ("text", "Credits"),
    ]
    for target_type, target_text in click_targets:
        try:
            if target_type == "text":
                page.get_by_text(target_text, exact=True).click(timeout=5000)
                break
        except Exception:
            continue
    else:
        try:
            page.goto(USAGE_URL.replace("#usage", "#balance"), wait_until="domcontentloaded", timeout=30000)
        except Exception:
            return None

    deadline = time.monotonic() + 20
    last_text = ""
    while time.monotonic() < deadline:
        try:
            text = page.locator("body").inner_text(timeout=5000)
            if text.strip():
                last_text = text
                credits = find_credit_value(line_list(text))
                if credits is not None:
                    write_debug_capture(text)
                    return credits
        except Exception:
            pass
        time.sleep(1)

    if last_text:
        write_debug_capture(last_text)
    return None


def parse_reset_time(reset_text: str | None, weekly: bool) -> str:
    if not reset_text:
        return ""

    now = datetime.now()
    text = reset_text.strip()
    date_match = re.search(r"(\d{4})\.\s*(\d{1,2})\.\s*(\d{1,2})\.", text)
    time_match = re.search(r"(오전|오후|AM|PM)?\s*(\d{1,2})\s*:\s*(\d{2})", text, re.I)
    if not time_match:
        return ""

    hour = int(time_match.group(2))
    minute = int(time_match.group(3))
    meridiem = (time_match.group(1) or "").lower()
    if meridiem in ("오후", "pm") and hour < 12:
        hour += 12
    if meridiem in ("오전", "am") and hour == 12:
        hour = 0

    if date_match:
        year = int(date_match.group(1))
        month = int(date_match.group(2))
        day = int(date_match.group(3))
        target = datetime(year, month, day, hour, minute)
    else:
        target = now.replace(hour=hour, minute=minute, second=0, microsecond=0)
        if target <= now:
            target += timedelta(days=7 if weekly else 1)

    return target.strftime("%Y-%m-%d %H:%M:%S")


def looks_logged_in(page) -> bool:
    url = page.url.lower()
    if "auth.openai.com" in url or "/auth/login" in url or "login" in url:
        return False
    try:
        text = page.locator("body").inner_text(timeout=3000)
    except Exception:
        return False
    return bool(re.search(r"(5\s*시간|주간|weekly|5-hour|5 hour)", text, re.I))


def wait_for_page_text(page, timeout_seconds: int = 45) -> str:
    deadline = time.monotonic() + timeout_seconds
    last_text = ""
    while time.monotonic() < deadline:
        try:
            text = page.locator("body").inner_text(timeout=5000)
            if text.strip():
                last_text = text
                if re.search(r"(\d{1,3})\s*%\s*(?:남음|remaining|left)", text, re.I):
                    return text
        except Exception:
            pass
        time.sleep(1)
    return last_text


def wait_for_manual_login(page) -> None:
    log("Waiting for manual login in visible browser window.")
    deadline = time.monotonic() + 15 * 60
    while time.monotonic() < deadline:
        if looks_logged_in(page):
            log("Manual login appears complete.")
            return
        time.sleep(5)
    raise TimeoutError("Login was not completed within 15 minutes.")


def collect_once(
    playwright,
    headless: bool,
    allow_manual_login: bool = False,
) -> dict[str, Any]:
    context = None
    try:
        context = playwright.chromium.launch_persistent_context(
            user_data_dir=str(PROFILE_DIR),
            headless=headless,
            viewport={"width": 1280, "height": 900},
            locale="ko-KR",
        )
        page = context.pages[0] if context.pages else context.new_page()
        page.goto(USAGE_URL, wait_until="domcontentloaded", timeout=60000)
        page.locator("body").wait_for(timeout=30000)
        if allow_manual_login and not looks_logged_in(page):
            wait_for_manual_login(page)
        page.goto(USAGE_URL, wait_until="domcontentloaded", timeout=60000)
        try:
            page.locator("body").wait_for(timeout=15000)
        except PlaywrightTimeoutError:
            pass
        text = wait_for_page_text(page)
        write_debug_capture(text)
        try:
            usage = parse_usage(text)
        except Exception:
            if DEBUG_CAPTURE:
                write_debug_capture(text)
                log(f"Saved page text for debugging: {PAGE_TEXT_PATH.name}")
            raise
        credits = collect_credits_from_balance_tab(page)
        if credits is not None:
            usage["creditsRemaining"] = credits
            log(f"Collected credits: {credits:g}")
        else:
            log("Credits were not found on the balance tab.")
        log(
            "Collected usage: 5h=%s weekly=%s"
            % (usage["fiveHourRemaining"], usage["weeklyRemaining"])
        )
        return usage
    finally:
        if context:
            context.close()


def collect_with_session_mode(playwright) -> dict[str, Any]:
    explicit_login = "--login" in sys.argv
    profile_initialized = PROFILE_DIR.exists() and any(PROFILE_DIR.iterdir())
    if explicit_login or not profile_initialized:
        log("Opening a visible browser for user login.")
        return collect_once(
            playwright,
            headless=False,
            allow_manual_login=True,
        )

    log("Trying standard headless browser collection with saved session.")
    return collect_once(
        playwright,
        headless=True,
        allow_manual_login=False,
    )


def main() -> int:
    log("Starting scraper.")
    lock_handle = acquire_lock()
    if lock_handle is None:
        return 0
    PID_PATH.write_text(str(os.getpid()), encoding="ascii")
    PROFILE_DIR.mkdir(parents=True, exist_ok=True)
    run_once = "--once" in sys.argv
    try:
        with sync_playwright() as playwright:
            while True:
                try:
                    usage = collect_with_session_mode(playwright)
                    write_usage(usage)
                    interval_seconds = get_interval_seconds()
                    log(f"Wrote {DATA_PATH.name}. Next check in {interval_seconds} seconds.")
                    if run_once:
                        return 0
                except KeyboardInterrupt:
                    log("Scraper stopped by keyboard interrupt.")
                    return 0
                except Exception as exc:
                    log(f"Collection failed: {exc}")
                    existing = read_existing_data()
                    existing.update(
                        {
                            "status": "error",
                            "lastError": str(exc),
                            "updatedAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                            "source": USAGE_URL,
                        }
                    )
                    write_usage(existing)
                    if run_once:
                        return 1
                time.sleep(get_interval_seconds())
    finally:
        try:
            msvcrt.locking(lock_handle.fileno(), msvcrt.LK_UNLCK, 1)
        finally:
            lock_handle.close()
            try:
                if PID_PATH.exists() and PID_PATH.read_text(encoding="ascii").strip() == str(os.getpid()):
                    PID_PATH.unlink()
            except OSError:
                pass


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        log(f"Fatal scraper error: {exc}")
        print(exc, file=sys.stderr)
        raise
