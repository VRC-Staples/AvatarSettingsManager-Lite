#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
import time
import traceback
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

try:
    import tkinter as tk
except Exception as exc:  # pragma: no cover - startup failure path
    tk = None
    TKINTER_IMPORT_ERROR = exc
else:
    TKINTER_IMPORT_ERROR = None

WINDOW_WIDTH = 460
WINDOW_MIN_HEIGHT = 260
WINDOW_MAX_HEIGHT = 920
WINDOW_MARGIN = 24
POLL_INTERVAL_MS = 150
CLOSE_GRACE_SECONDS = 1.25
IDLE_TIMEOUT_SECONDS = 120.0
BACKGROUND = "#10161d"
PANEL_BACKGROUND = "#16202a"
REVIEW_BACKGROUND = "#1a2430"
TEXT_PRIMARY = "#f4f7fb"
TEXT_MUTED = "#b6c4d4"
TEXT_WARNING = "#ffd68a"
BUTTON_BACKGROUND = "#2a89c9"
BUTTON_ACTIVE = "#39a1e6"
BUTTON_DISABLED = "#365064"
CONTENT_WRAP_PADDING = 80
CHECKLIST_BADGE_WIDTH = 108
CHECKLIST_GLYPH_WIDTH = 28
CHECKLIST_COLUMN_GAP = 12
CHECKLIST_TEXT_MIN_WRAP = 180
STATUS_STYLES = {
    "Running": {"badge": "RUNNING", "accent": "#2bb4c5", "badge_bg": "#1d5166"},
    "Success": {"badge": "PASSED", "accent": "#32c06a", "badge_bg": "#245737"},
    "Failure": {"badge": "FAILED", "accent": "#dc5b5b", "badge_bg": "#6e2b2b"},
    "Warning": {"badge": "NOTICE", "accent": "#d9aa39", "badge_bg": "#67521d"},
}
CHECKLIST_STYLES = {
    "Pending": {"glyph": "•", "color": "#9fb0c2"},
    "Active": {"glyph": "→", "color": "#6bc2ff"},
    "Completed": {"glyph": "✓", "color": "#79db93"},
    "Failed": {"glyph": "✕", "color": "#ff8b8b"},
    "Warning": {"glyph": "!", "color": "#ffd36e"},
}


def utc_ticks_now() -> int:
    unix_seconds = time.time()
    return int((unix_seconds + 62135596800) * 10_000_000)


def build_visual_state_signature(state: dict[str, Any]) -> str:
    normalized_state = dict(state)
    normalized_state.pop("updatedUtcTicks", None)
    return json.dumps(normalized_state, sort_keys=True, separators=(",", ":"))


def calculate_content_wraplength(window_width: int) -> int:
    return max(220, window_width - CONTENT_WRAP_PADDING)


def calculate_checklist_text_wraplength(window_width: int) -> int:
    reserved_width = CONTENT_WRAP_PADDING + CHECKLIST_BADGE_WIDTH + CHECKLIST_GLYPH_WIDTH + CHECKLIST_COLUMN_GAP * 2
    return max(CHECKLIST_TEXT_MIN_WRAP, window_width - reserved_width)


@dataclass(frozen=True)
class OverlayPaths:
    state_path: Path
    ack_path: Path
    log_path: Path | None


class Logger:
    def __init__(self, log_path: Path | None) -> None:
        self._log_path = log_path

    def write(self, message: str) -> None:
        if self._log_path is None:
            return

        try:
            self._log_path.parent.mkdir(parents=True, exist_ok=True)
            timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
            with self._log_path.open("a", encoding="utf-8") as handle:
                handle.write(f"[{timestamp}Z] {message}\n")
        except Exception:
            pass


class VisibleOverlayApp:
    def __init__(self, paths: OverlayPaths) -> None:
        if tk is None:
            raise RuntimeError(f"tkinter is required to launch the visible overlay: {TKINTER_IMPORT_ERROR}")

        self.paths = paths
        self.logger = Logger(paths.log_path)
        self.root = tk.Tk()
        self.root.title("ASM-Lite Visible Smoke")
        self.root.configure(bg=BACKGROUND)
        self.root.resizable(False, False)
        self.root.attributes("-topmost", True)

        self.root.protocol("WM_DELETE_WINDOW", self.close)
        self.root.bind("<Escape>", lambda _event: self.close())

        self._last_render_signature: str | None = None
        self._last_rendered_state: dict[str, Any] | None = None
        self._close_after: float | None = None
        self._last_state_seen_at = time.monotonic()
        self._has_seen_active_session = False
        self._active_ack_target: tuple[str, int] | None = None
        self._last_footer_text = ""
        self._last_window_geometry = ""
        self._closed = False

        self._build_ui()
        self._render_waiting_state()
        self.logger.write(f"Overlay started with state path '{self.paths.state_path}' and ack path '{self.paths.ack_path}'.")

    def _build_ui(self) -> None:
        self.outer = tk.Frame(self.root, bg=BACKGROUND, padx=14, pady=14)
        self.outer.pack(fill="both", expand=True)

        self.header = tk.Frame(self.outer, bg=PANEL_BACKGROUND, padx=14, pady=12, highlightthickness=0)
        self.header.pack(fill="x")

        self.accent = tk.Frame(self.header, bg=STATUS_STYLES["Running"]["accent"], height=4)
        self.accent.pack(fill="x", side="top", pady=(0, 10))

        title_row = tk.Frame(self.header, bg=PANEL_BACKGROUND)
        title_row.pack(fill="x")

        title_column = tk.Frame(title_row, bg=PANEL_BACKGROUND)
        title_column.pack(side="left", fill="x", expand=True)

        self.title_label = tk.Label(
            title_column,
            text="ASM-Lite visible smoke",
            anchor="w",
            justify="left",
            bg=PANEL_BACKGROUND,
            fg=TEXT_PRIMARY,
            font=("Segoe UI", 14, "bold"),
        )
        self.title_label.pack(fill="x")

        self.meta_label = tk.Label(
            title_column,
            text="Waiting for Unity…",
            anchor="w",
            justify="left",
            bg=PANEL_BACKGROUND,
            fg=TEXT_MUTED,
            font=("Segoe UI", 10),
        )
        self.meta_label.pack(fill="x", pady=(2, 0))

        self.badge_label = tk.Label(
            title_row,
            text="RUNNING",
            bg=STATUS_STYLES["Running"]["badge_bg"],
            fg=TEXT_PRIMARY,
            padx=10,
            pady=4,
            font=("Segoe UI", 9, "bold"),
        )
        self.badge_label.pack(side="right", anchor="n")

        self.step_label = tk.Label(
            self.header,
            text="Waiting for Unity to publish visible smoke state…",
            anchor="w",
            justify="left",
            wraplength=calculate_content_wraplength(WINDOW_WIDTH),
            bg=PANEL_BACKGROUND,
            fg=TEXT_PRIMARY,
            font=("Segoe UI", 12),
        )
        self.step_label.pack(fill="x", pady=(12, 0))

        self.checklist_header = tk.Label(
            self.outer,
            text="Visible Smoke Checklist",
            anchor="w",
            justify="left",
            bg=BACKGROUND,
            fg=TEXT_PRIMARY,
            font=("Segoe UI", 11, "bold"),
        )
        self.checklist_header.pack(fill="x", pady=(12, 0))

        self.checklist_meta = tk.Label(
            self.outer,
            text="Waiting for checklist items…",
            anchor="w",
            justify="left",
            bg=BACKGROUND,
            fg=TEXT_MUTED,
            font=("Segoe UI", 9),
        )
        self.checklist_meta.pack(fill="x", pady=(2, 0))

        self.checklist_view = tk.Frame(self.outer, bg=BACKGROUND)
        self.checklist_view.pack(fill="both", expand=True, pady=(8, 0))

        self.checklist_scroll = tk.Canvas(self.checklist_view, bg=BACKGROUND, highlightthickness=0, bd=0)
        self.checklist_scroll.pack(side="left", fill="both", expand=True)

        self.checklist_scrollbar = tk.Scrollbar(self.checklist_view, orient="vertical", command=self.checklist_scroll.yview)
        self.checklist_scroll.configure(yscrollcommand=self._handle_checklist_yview)

        self.checklist_frame = tk.Frame(self.checklist_scroll, bg=BACKGROUND)
        self._checklist_window_id = self.checklist_scroll.create_window((0, 0), window=self.checklist_frame, anchor="nw")
        self.checklist_frame.bind("<Configure>", self._handle_checklist_frame_configure)
        self.checklist_scroll.bind("<Configure>", self._handle_checklist_canvas_configure)

        self.review_frame = tk.Frame(self.outer, bg=REVIEW_BACKGROUND, padx=14, pady=12)
        self.review_title = tk.Label(
            self.review_frame,
            text="Visible smoke results ready",
            anchor="w",
            justify="left",
            bg=REVIEW_BACKGROUND,
            fg=TEXT_PRIMARY,
            font=("Segoe UI", 12, "bold"),
        )
        self.review_title.pack(fill="x")

        self.review_message = tk.Label(
            self.review_frame,
            text="",
            anchor="w",
            justify="left",
            wraplength=calculate_content_wraplength(WINDOW_WIDTH),
            bg=REVIEW_BACKGROUND,
            fg=TEXT_PRIMARY,
            font=("Segoe UI", 10),
        )
        self.review_message.pack(fill="x", pady=(10, 0))

        self.review_hint = tk.Label(
            self.review_frame,
            text="Review the overlay, then click Accept and close to approve this run.",
            anchor="w",
            justify="left",
            wraplength=calculate_content_wraplength(WINDOW_WIDTH),
            bg=REVIEW_BACKGROUND,
            fg=TEXT_MUTED,
            font=("Segoe UI", 9),
        )
        self.review_hint.pack(fill="x", pady=(10, 0))

        self.accept_button = tk.Button(
            self.review_frame,
            text="Accept and close",
            command=self._write_acknowledgement,
            bg=BUTTON_BACKGROUND,
            fg=TEXT_PRIMARY,
            activebackground=BUTTON_ACTIVE,
            activeforeground=TEXT_PRIMARY,
            disabledforeground=TEXT_MUTED,
            relief="flat",
            bd=0,
            padx=10,
            pady=6,
            font=("Segoe UI", 10, "bold"),
        )
        self.accept_button.pack(anchor="e", pady=(12, 0))

        self.footer_label = tk.Label(
            self.outer,
            text="",
            anchor="w",
            justify="left",
            bg=BACKGROUND,
            fg=TEXT_WARNING,
            font=("Segoe UI", 8),
        )
        self.footer_label.pack(fill="x", pady=(10, 0))

    def _handle_checklist_frame_configure(self, _event: Any) -> None:
        self.checklist_scroll.configure(scrollregion=self.checklist_scroll.bbox("all"))
        self._sync_checklist_scrollbar_visibility()

    def _handle_checklist_canvas_configure(self, event: Any) -> None:
        self.checklist_scroll.itemconfigure(self._checklist_window_id, width=event.width)
        self._sync_checklist_scrollbar_visibility()

    def _handle_checklist_yview(self, first: str, last: str) -> None:
        self.checklist_scrollbar.set(first, last)
        self._sync_checklist_scrollbar_visibility(first, last)

    def _sync_checklist_scrollbar_visibility(self, first: str | None = None, last: str | None = None) -> None:
        if first is None or last is None:
            first, last = self.checklist_scroll.yview()

        needs_scrollbar = float(first) > 0.0 or float(last) < 1.0
        scrollbar_visible = bool(self.checklist_scrollbar.winfo_manager())
        if needs_scrollbar == scrollbar_visible:
            return

        if needs_scrollbar:
            self.checklist_scrollbar.pack(side="right", fill="y")
        else:
            self.checklist_scrollbar.pack_forget()

    def run(self) -> int:
        self.root.after(0, self._poll)
        self.root.mainloop()
        return 0

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        self.logger.write("Overlay shutting down.")
        try:
            self.root.destroy()
        except Exception:
            pass

    def _render_waiting_state(self) -> None:
        self._apply_status_style("Running")
        self.title_label.configure(text="ASM-Lite visible smoke")
        self.meta_label.configure(text="Waiting for Unity to publish overlay state…")
        self.step_label.configure(text="Waiting for Unity to publish visible smoke state…")
        self.checklist_meta.configure(text="Waiting for checklist items…")
        self._render_checklist([])
        self.review_frame.pack_forget()
        self.footer_label.configure(text=f"Watching {self.paths.state_path}")
        self._reposition_window()

    def _poll(self) -> None:
        if self._closed:
            return

        try:
            state = self._read_state_document()
            now = time.monotonic()

            if state is None:
                if self._has_seen_active_session and now - self._last_state_seen_at >= IDLE_TIMEOUT_SECONDS:
                    self.logger.write("Closing overlay because Unity stopped updating the state file.")
                    self.close()
                    return
                self.root.after(POLL_INTERVAL_MS, self._poll)
                return

            self._last_state_seen_at = now
            session_active = bool(state.get("sessionActive")) or bool(state.get("completionReviewVisible"))
            if session_active:
                self._has_seen_active_session = True
                self._close_after = None
            elif self._has_seen_active_session:
                if self._close_after is None:
                    self._close_after = now + CLOSE_GRACE_SECONDS
                elif now >= self._close_after:
                    self.logger.write("Closing overlay after Unity marked the session inactive.")
                    self.close()
                    return

            self._render_state(state)
        except Exception as exc:  # pragma: no cover - runtime safety path
            self.logger.write(f"Overlay update failed: {exc}\n{traceback.format_exc()}")
            self.footer_label.configure(text=f"Overlay error: {exc}")

        if not self._closed:
            self.root.after(POLL_INTERVAL_MS, self._poll)

    def _read_state_document(self) -> dict[str, Any] | None:
        if not self.paths.state_path.exists():
            return None

        try:
            raw = self.paths.state_path.read_text(encoding="utf-8-sig")
        except OSError as exc:
            self.logger.write(f"Failed to read state file: {exc}")
            return None

        if not raw.strip():
            return None

        try:
            return json.loads(raw)
        except json.JSONDecodeError as exc:
            self.logger.write(f"Ignoring incomplete state payload: {exc}")
            return None

    def _render_state(self, state: dict[str, Any]) -> None:
        signature = build_visual_state_signature(state)
        footer_text = self._build_footer_text(state)
        if signature == self._last_render_signature:
            self._update_footer_label(footer_text)
            return

        self._last_render_signature = signature
        self._last_rendered_state = state

        status_key = str(state.get("state") or "Running")
        self._apply_status_style(status_key)

        title = str(state.get("title") or "ASM-Lite visible smoke")
        self.title_label.configure(text=title)
        self.meta_label.configure(text=self._build_meta_text(state))
        self.step_label.configure(text=str(state.get("step") or "Waiting for Unity…"))

        checklist = state.get("checklist")
        checklist_items = checklist if isinstance(checklist, list) else []
        completed = sum(1 for item in checklist_items if str(item.get("state") or "") == "Completed")
        if checklist_items:
            self.checklist_meta.configure(text=f"Completed {completed}/{len(checklist_items)} • Published {len(checklist_items)} items")
        else:
            self.checklist_meta.configure(text="No checklist items published yet")
        self._render_checklist(checklist_items)

        if bool(state.get("completionReviewVisible")):
            request_id = int(state.get("completionReviewRequestId") or 0)
            session_id = str(state.get("sessionId") or "")
            self.review_title.configure(text=str(state.get("completionReviewTitle") or "Visible smoke results ready"))
            self.review_message.configure(text=str(state.get("completionReviewMessage") or "Review the overlay, then accept the run."))
            self.review_frame.pack(fill="x", pady=(12, 0))

            ack_target = (session_id, request_id)
            if self._active_ack_target == ack_target:
                self.accept_button.configure(state="disabled", text="Awaiting Unity…", bg=BUTTON_DISABLED)
            else:
                self.accept_button.configure(state="normal", text="Accept and close", bg=BUTTON_BACKGROUND)
        else:
            self._active_ack_target = None
            self.review_frame.pack_forget()
            self.accept_button.configure(state="normal", text="Accept and close", bg=BUTTON_BACKGROUND)

        self._update_footer_label(footer_text)
        self._reposition_window()

    def _build_footer_text(self, state: dict[str, Any]) -> str:
        updated_ticks = int(state.get("updatedUtcTicks") or 0)
        checklist = state.get("checklist")
        checklist_items = checklist if isinstance(checklist, list) else []
        footer_parts = [f"Updated {self._format_ticks(updated_ticks)}"]
        if checklist_items:
            footer_parts.append(f"Checklist items {len(checklist_items)}")
        footer = " • ".join(footer_parts)
        if not bool(state.get("sessionActive")) and self._has_seen_active_session:
            footer = f"Session complete. Closing shortly. {footer}"
        return footer

    def _update_footer_label(self, footer_text: str) -> None:
        if footer_text == self._last_footer_text:
            return

        self._last_footer_text = footer_text
        self.footer_label.configure(text=footer_text)
        self._reposition_window()

    def _apply_status_style(self, status_key: str) -> None:
        style = STATUS_STYLES.get(status_key, STATUS_STYLES["Running"])
        badge = style["badge"]
        self.badge_label.configure(text=badge, bg=style["badge_bg"])
        self.accent.configure(bg=style["accent"])

    def _build_meta_text(self, state: dict[str, Any]) -> str:
        parts: list[str] = []
        step_index = int(state.get("stepIndex") or 0)
        total_steps = int(state.get("totalSteps") or 0)
        if step_index > 0 and total_steps > 0:
            parts.append(f"Step {step_index}/{total_steps}")
        elif step_index > 0:
            parts.append(f"Step {step_index}")
        if bool(state.get("presentationMode")):
            parts.append("Presentation Mode")
        return " • ".join(parts) if parts else "Visible smoke overlay"

    def _render_checklist(self, checklist_items: list[dict[str, Any]]) -> None:
        for child in list(self.checklist_frame.winfo_children()):
            child.destroy()

        if not checklist_items:
            placeholder = tk.Label(
                self.checklist_frame,
                text="Waiting for checklist items…",
                anchor="w",
                justify="left",
                bg=BACKGROUND,
                fg=TEXT_MUTED,
                font=("Segoe UI", 10),
            )
            placeholder.pack(fill="x")
            return

        for index, item in enumerate(checklist_items, start=1):
            state_key = str(item.get("state") or "Pending")
            style = CHECKLIST_STYLES.get(state_key, CHECKLIST_STYLES["Pending"])
            row = tk.Frame(self.checklist_frame, bg=BACKGROUND)
            row.pack(fill="x", pady=(0, 6))
            row.grid_columnconfigure(1, weight=1)

            glyph = tk.Label(
                row,
                text=style["glyph"],
                width=2,
                anchor="nw",
                justify="left",
                bg=BACKGROUND,
                fg=style["color"],
                font=("Segoe UI Symbol", 12, "bold"),
            )
            glyph.grid(row=0, column=0, sticky="nw", padx=(0, CHECKLIST_COLUMN_GAP))

            text = tk.Label(
                row,
                text=f"{index}. {str(item.get('text') or '').strip()}",
                anchor="w",
                justify="left",
                wraplength=calculate_checklist_text_wraplength(WINDOW_WIDTH),
                bg=BACKGROUND,
                fg=TEXT_PRIMARY if state_key != "Pending" else TEXT_MUTED,
                font=("Segoe UI", 10),
            )
            text.grid(row=0, column=1, sticky="ew")

            badge = tk.Label(
                row,
                text=state_key.upper(),
                width=11,
                anchor="e",
                justify="right",
                bg=BACKGROUND,
                fg=style["color"],
                font=("Segoe UI", 8, "bold"),
            )
            badge.grid(row=0, column=2, sticky="ne", padx=(CHECKLIST_COLUMN_GAP, 0))

    def _write_acknowledgement(self) -> None:
        if self._last_rendered_state is None:
            return

        session_id = str(self._last_rendered_state.get("sessionId") or "")
        request_id = int(self._last_rendered_state.get("completionReviewRequestId") or 0)
        if not session_id or request_id <= 0:
            self.logger.write("Ignoring acknowledgement request because session id or request id is missing.")
            return

        payload = {
            "sessionId": session_id,
            "completionReviewRequestId": request_id,
            "acknowledged": True,
            "acknowledgedUtcTicks": utc_ticks_now(),
        }

        try:
            self.paths.ack_path.parent.mkdir(parents=True, exist_ok=True)
            self.paths.ack_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        except OSError as exc:
            self.logger.write(f"Failed to write acknowledgement: {exc}")
            self.footer_label.configure(text=f"Failed to write acknowledgement: {exc}")
            return

        self._active_ack_target = (session_id, request_id)
        self.accept_button.configure(state="disabled", text="Awaiting Unity…", bg=BUTTON_DISABLED)
        self.footer_label.configure(text=f"Sent acknowledgement for review request {request_id}.")
        self.logger.write(f"Acknowledgement written for session '{session_id}' request {request_id}.")

    def _reposition_window(self) -> None:
        self.root.update_idletasks()
        width = WINDOW_WIDTH
        height = max(WINDOW_MIN_HEIGHT, min(WINDOW_MAX_HEIGHT, self.root.winfo_reqheight() + 8))
        x = max(WINDOW_MARGIN, self.root.winfo_screenwidth() - width - WINDOW_MARGIN)
        y = WINDOW_MARGIN
        geometry = f"{width}x{height}+{x}+{y}"
        if geometry == self._last_window_geometry:
            return

        self._last_window_geometry = geometry
        self.root.geometry(geometry)

    @staticmethod
    def _format_ticks(updated_ticks: int) -> str:
        if updated_ticks <= 0:
            return "just now"
        try:
            unix_seconds = (updated_ticks - 621355968000000000) / 10_000_000
            stamp = datetime.fromtimestamp(unix_seconds, tz=timezone.utc)
            return stamp.strftime("%H:%M:%S UTC")
        except Exception:
            return "just now"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Render the ASM-Lite visible smoke overlay from a JSON state file.")
    parser.add_argument("--state-path", required=True, help="Path to the Unity-written JSON state file.")
    parser.add_argument("--ack-path", required=True, help="Path to the JSON acknowledgement file written after acceptance.")
    parser.add_argument("--log-path", default="", help="Optional log file for overlay startup and acknowledgement events.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    paths = OverlayPaths(
        state_path=Path(args.state_path).expanduser(),
        ack_path=Path(args.ack_path).expanduser(),
        log_path=Path(args.log_path).expanduser() if args.log_path else None,
    )
    app = VisibleOverlayApp(paths)
    return app.run()


if __name__ == "__main__":
    raise SystemExit(main())
