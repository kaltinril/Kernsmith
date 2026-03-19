"""
Extract user prompt history from Claude Code conversation transcripts.

Reads all JSONL session files for a given project and outputs just the
text the user typed into the prompt window, ordered chronologically.

Usage:
    python scripts/extract_prompt_history.py [project_name]

    If no project_name is given, auto-detects from the current working
    directory by converting the absolute path to Claude Code's folder
    naming convention (e.g. c:\git\monoai -> c--git-monoai).

    Output is written to prompt_history_<project_name>.txt in the cwd.
"""

import json
import os
import sys
import glob
import re
from datetime import datetime

CLAUDE_DIR = os.path.expanduser("~/.claude/projects")

# Tags injected by the IDE/system into user text content blocks.
# These appear alongside real user text and need to be stripped out.
IDE_TAG_PATTERNS = [
    r"<ide_opened_file>.*?</ide_opened_file>",
    r"<ide_selection>.*?</ide_selection>",
]


def strip_ide_tags(text):
    """Remove IDE-injected tags from a user text block."""
    for pattern in IDE_TAG_PATTERNS:
        text = re.sub(pattern, "", text, flags=re.DOTALL)
    return text.strip()


def detect_project_name():
    """Convert cwd to Claude Code's project folder name.

    e.g. C:/git/monoai -> c--git-monoai
    """
    cwd = os.getcwd().replace("\\", "/")
    # Remove trailing slash, lowercase, replace :/ and / with --
    cwd = cwd.rstrip("/").lower()
    # "C:/git/monoai" -> "c--git-monoai"
    name = cwd.replace(":/", "--").replace("/", "-")
    return name


def extract_prompts(project_name):
    project_dir = os.path.join(CLAUDE_DIR, project_name)
    if not os.path.isdir(project_dir):
        print(f"Error: project directory not found: {project_dir}")
        print(f"Available projects:")
        for d in sorted(os.listdir(CLAUDE_DIR)):
            if os.path.isdir(os.path.join(CLAUDE_DIR, d)) and d != "memory":
                print(f"  {d}")
        sys.exit(1)

    jsonl_files = sorted(glob.glob(os.path.join(project_dir, "*.jsonl")))
    if not jsonl_files:
        print(f"No conversation files found in {project_dir}")
        sys.exit(1)

    all_sessions = []

    for fpath in jsonl_files:
        session_prompts = []
        session_time = None

        with open(fpath, "r", encoding="utf-8") as f:
            for line in f:
                try:
                    data = json.loads(line)
                except (json.JSONDecodeError, ValueError):
                    continue

                if data.get("type") != "user":
                    continue

                # Skip tool approval results — never user-typed
                if "toolUseResult" in data:
                    continue

                msg = data.get("message", {})
                content = msg.get("content", "")
                ts = data.get("timestamp")

                # String content = always system-injected (task notifications,
                # context recovery, slash commands). Skip entirely.
                if isinstance(content, str):
                    continue

                texts = []
                if isinstance(content, list):
                    for item in content:
                        if isinstance(item, dict) and item.get("type") == "text":
                            cleaned = strip_ide_tags(item.get("text", ""))
                            if cleaned:
                                texts.append(cleaned)

                user_text = "\n".join(texts).strip()
                if user_text:
                    if session_time is None and ts:
                        session_time = ts
                    session_prompts.append(user_text)

        if session_prompts:
            mtime = os.path.getmtime(fpath)
            all_sessions.append(
                {
                    "file": os.path.basename(fpath),
                    "mtime": mtime,
                    "timestamp": session_time,
                    "prompts": session_prompts,
                }
            )

    # Sort chronologically
    all_sessions.sort(key=lambda s: s["mtime"])
    return all_sessions


def format_output(sessions):
    lines = []
    for i, session in enumerate(sessions, 1):
        dt = datetime.fromtimestamp(session["mtime"])
        lines.append("=" * 60)
        lines.append(f"SESSION {i} -- {dt.strftime('%Y-%m-%d %H:%M')}")
        lines.append("=" * 60)
        for j, prompt in enumerate(session["prompts"], 1):
            lines.append(f"\n--- Prompt {j} ---")
            lines.append(prompt)
        lines.append("")
    return "\n".join(lines)


def main():
    project_name = sys.argv[1] if len(sys.argv) > 1 else detect_project_name()
    sessions = extract_prompts(project_name)

    total_prompts = sum(len(s["prompts"]) for s in sessions)
    print(f"Extracted {total_prompts} prompts from {len(sessions)} sessions")

    output = format_output(sessions)
    outfile = os.path.join(os.getcwd(), f"prompt_history_{project_name}.txt")
    with open(outfile, "w", encoding="utf-8") as f:
        f.write(output)

    print(f"Written to: {outfile}")
    print(f"File size: {os.path.getsize(outfile):,} bytes")


if __name__ == "__main__":
    main()
