#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Minimal adb CLI shim backed by MuMuManager sh (when MuMu NAT adbd is broken)."""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

MM = os.environ.get(
    "MUMU_MANAGER",
    r"C:\Program Files\Netease\MuMu\nx_main\MuMuManager.exe",
)
VM = os.environ.get("MUMU_VM", "1")
SHARE = Path(
    os.environ.get(
        "MUMU_SHARE",
        r"C:\Users\Administrator\Documents\MuMu共享文件夹",
    )
)
REMOTE_SHARE = "/mnt/shared/MuMuShared"
SERIAL_ALIAS = {"127.0.0.1:16416", "emulator-5554", "mumu-1"}


def msh(cmd: str, timeout: float = 60) -> tuple[int, bytes]:
    r = subprocess.run(
        [MM, "sh", "-v", VM, "-c", cmd],
        capture_output=True,
        timeout=timeout,
    )
    return r.returncode, (r.stdout or b"") + (r.stderr or b"")


def out(data: bytes | str, code: int = 0) -> int:
    if isinstance(data, str):
        data = data.encode("utf-8", errors="replace")
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()
    return code


def parse_args(argv: list[str]) -> tuple[str | None, list[str]]:
    serial = None
    args = list(argv)
    i = 0
    while i < len(args):
        if args[i] == "-s" and i + 1 < len(args):
            serial = args[i + 1]
            del args[i : i + 2]
            continue
        if args[i].startswith("-s") and len(args[i]) > 2:
            serial = args[i][2:]
            del args[i]
            continue
        i += 1
    return serial, args


def cmd_devices(_: list[str]) -> int:
    return out("List of devices attached\n127.0.0.1:16416\tdevice\n")


def cmd_connect(args: list[str]) -> int:
    return out(f"connected to {args[0] if args else '127.0.0.1:16416'}\n")


def cmd_disconnect(_: list[str]) -> int:
    return out("disconnected\n")


def cmd_get_state(_: list[str]) -> int:
    return out("device\n")


def cmd_version(_: list[str]) -> int:
    return out(
        "Android Debug Bridge version 1.0.41\n"
        "Version 36.0.0-mumu-shim\n"
        "Installed as mumu_adb\n"
    )


def shell_join(args: list[str]) -> str:
    # adb shell may be: shell <cmd...> OR shell (interactive ignored)
    if not args:
        return "echo"
    # If single arg with spaces, keep as-is; else join
    if len(args) == 1:
        return args[0]
    # Quote lightly for sh -c
    parts = []
    for a in args:
        if any(c in a for c in ' \t"$&;|<>()'):
            parts.append("'" + a.replace("'", "'\\''") + "'")
        else:
            parts.append(a)
    return " ".join(parts)


def cmd_shell(args: list[str]) -> int:
    code, data = msh(shell_join(args))
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()
    return 0 if code == 0 else code


def cmd_exec_out(args: list[str]) -> int:
    # Used heavily for: exec-out screencap -p
    if args[:1] == ["screencap"] or (args and args[0] == "screencap"):
        return screencap_stdout()
    code, data = msh(shell_join(args))
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()
    return 0 if code == 0 else code


def screencap_stdout() -> int:
    name = f"_adb_sc_{os.getpid()}.png"
    remote = f"{REMOTE_SHARE}/{name}"
    host = SHARE / name
    if host.exists():
        try:
            host.unlink()
        except OSError:
            pass
    code, _ = msh(f"screencap -p {remote}")
    for _ in range(40):
        if host.exists() and host.stat().st_size > 1000:
            data = host.read_bytes()
            try:
                host.unlink()
            except OSError:
                pass
            msh(f"rm -f {remote}", timeout=10)
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()
            return 0
        time.sleep(0.1)
    return out(b"", 1)


def cmd_pull(args: list[str]) -> int:
    if len(args) < 1:
        return out("adb: pull requires remote path\n", 1)
    remote = args[0]
    local = args[1] if len(args) > 1 else Path(remote).name
    name = f"_adb_pull_{os.getpid()}{Path(remote).suffix or '.bin'}"
    staged = f"{REMOTE_SHARE}/{name}"
    host_staged = SHARE / name
    code, data = msh(f"cp '{remote}' '{staged}' && chmod 666 '{staged}'")
    if code != 0:
        sys.stderr.buffer.write(data or b"pull failed\n")
        return 1
    for _ in range(40):
        if host_staged.exists() and host_staged.stat().st_size >= 0:
            shutil.copy2(host_staged, local)
            try:
                host_staged.unlink()
            except OSError:
                pass
            msh(f"rm -f {staged}", timeout=10)
            return out(f"{remote}: 1 file pulled.\n")
        time.sleep(0.1)
    return out("adb: failed to pull\n", 1)


def cmd_push(args: list[str]) -> int:
    if len(args) < 2:
        return out("adb: push requires local and remote\n", 1)
    local, remote = args[0], args[1]
    if not Path(local).exists():
        return out(f"adb: cannot stat '{local}'\n", 1)
    name = f"_adb_push_{os.getpid()}_{Path(local).name}"
    host_staged = SHARE / name
    shutil.copy2(local, host_staged)
    staged = f"{REMOTE_SHARE}/{name}"
    # ensure remote dir
    remote_dir = str(Path(remote).parent).replace("\\", "/")
    code, data = msh(
        f"mkdir -p '{remote_dir}' && cp '{staged}' '{remote}' && chmod 755 '{remote}' && rm -f '{staged}'"
    )
    try:
        host_staged.unlink()
    except OSError:
        pass
    if code != 0:
        sys.stderr.buffer.write(data or b"push failed\n")
        return 1
    return out(f"{local}: 1 file pushed.\n")


def cmd_forward(args: list[str]) -> int:
    # no-op success for minitouch leftovers
    return out("")


def cmd_kill_server(_: list[str]) -> int:
    return 0


def cmd_start_server(_: list[str]) -> int:
    return 0


def main(argv: list[str]) -> int:
    _, args = parse_args(argv)
    if not args:
        return cmd_version([])
    cmd = args[0]
    rest = args[1:]
    table = {
        "devices": cmd_devices,
        "connect": cmd_connect,
        "disconnect": cmd_disconnect,
        "get-state": cmd_get_state,
        "version": cmd_version,
        "shell": cmd_shell,
        "exec-out": cmd_exec_out,
        "pull": cmd_pull,
        "push": cmd_push,
        "forward": cmd_forward,
        "kill-server": cmd_kill_server,
        "start-server": cmd_start_server,
        "wait-for-device": lambda _: out(""),
        "reboot": lambda a: cmd_shell(["reboot"] + a),
    }
    if cmd in table:
        return table[cmd](rest)
    # unknown: try as shell passthrough for safety
    return out(f"adb: unknown command {cmd}\n", 1)


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except subprocess.TimeoutExpired:
        raise SystemExit(1)
