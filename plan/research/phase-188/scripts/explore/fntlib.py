"""Shared .fnt + atlas loader for the explore scripts (import via sys.path.insert; scripts run with -I)."""
import os, re
from PIL import Image
import numpy as np

RENDERS = os.environ.get("KERNSMITH_RESEARCH_OUT", "output/research") + "/renders"
WT = os.environ.get("KERNSMITH_ROOT", ".")
FONT_FILES = {
    "roboto": WT + "/tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf",
    "verdana": "C:/Windows/Fonts/verdana.ttf",
    "tahoma": "C:/Windows/Fonts/tahoma.ttf",
    "georgia": "C:/Windows/Fonts/georgia.ttf",
    "arial": "C:/Windows/Fonts/arial.ttf",
    "notosans": WT + "/tests/KernSmith.Tests/Fixtures/NotoSans-Regular-Latin.ttf",
    "sourceserif": WT + "/tests/KernSmith.Tests/Fixtures/SourceSerif4-Regular.otf",
}

class Fnt:
    def __init__(self, base):
        self.base = base
        self.chars, self.kern = {}, {}
        path = base + ".fnt"
        if not os.path.exists(path):
            raise FileNotFoundError(path)
        for line in open(path, encoding="utf-8", errors="replace"):
            t = line.split()[0] if line.strip() else ""
            kv = dict(re.findall(r'(\w+)=("[^"]*"|\S+)', line))
            if t == "common":
                self.line_height = int(kv["lineHeight"]); self.base_line = int(kv["base"])
            elif t == "info":
                self.size = int(kv["size"])
            elif t == "char":
                self.chars[int(kv["id"])] = {k: int(v) for k, v in kv.items() if not v.startswith('"')}
            elif t == "kerning":
                self.kern[(int(kv["first"]), int(kv["second"]))] = int(kv["amount"])
        img = Image.open(base + "_0.png").convert("RGBA")
        a = np.array(img)
        # alpha if the page has one, else luminance
        self.atlas = a[..., 3] if a[..., 3].min() < 255 else a[..., :3].mean(axis=2).astype(np.uint8)

    def glyph(self, ch):
        c = self.chars.get(ord(ch))
        if not c or c["width"] == 0 or c["height"] == 0:
            return None, c
        return self.atlas[c["y"]:c["y"] + c["height"], c["x"]:c["x"] + c["width"]], c

    def place(self, ch, canvas_w=None, canvas_h=None):
        """Glyph on a canvas aligned to pen x=0 and baseline at row base_line."""
        g, c = self.glyph(ch)
        if c is None:
            return None
        h = canvas_h or self.line_height + 4
        w = canvas_w or max(c["xadvance"], c["xoffset"] + c["width"]) + 2
        out = np.zeros((h, w), dtype=np.uint8)
        if g is not None:
            y0 = c["yoffset"]; x0 = max(c["xoffset"], 0)
            gh, gw = g.shape
            out[y0:y0 + gh, x0:x0 + gw] = g[: h - y0, : w - x0]
        return out

def load(font, size, variant, aa):
    return Fnt(os.path.join(RENDERS, f"{font}_{size}_{variant}_{aa}"))

def ink_rows(g, thr=128):
    rows = np.where((g >= thr).any(axis=1))[0]
    return (rows.min(), rows.max()) if len(rows) else (None, None)

def ink_cols(g, thr=128):
    cols = np.where((g >= thr).any(axis=0))[0]
    return (cols.min(), cols.max()) if len(cols) else (None, None)

def holes(bits):
    """Number of enclosed background regions (4-connected background not touching border)."""
    h, w = bits.shape
    bg = ~bits
    seen = np.zeros_like(bg, dtype=bool)
    from collections import deque
    def flood(sy, sx):
        q = deque([(sy, sx)]); seen[sy, sx] = True
        while q:
            y, x = q.popleft()
            for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                ny, nx = y+dy, x+dx
                if 0 <= ny < h and 0 <= nx < w and bg[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q.append((ny, nx))
    for y in range(h):
        for x in (0, w-1):
            if bg[y, x] and not seen[y, x]: flood(y, x)
    for x in range(w):
        for y in (0, h-1):
            if bg[y, x] and not seen[y, x]: flood(y, x)
    n = 0
    for y in range(h):
        for x in range(w):
            if bg[y, x] and not seen[y, x]:
                n += 1; flood(y, x)
    return n

def components(bits):
    """8-connected ink components."""
    h, w = bits.shape
    seen = np.zeros_like(bits, dtype=bool)
    from collections import deque
    n = 0
    for y in range(h):
        for x in range(w):
            if bits[y, x] and not seen[y, x]:
                n += 1; q = deque([(y, x)]); seen[y, x] = True
                while q:
                    cy, cx = q.popleft()
                    for dy in (-1,0,1):
                        for dx in (-1,0,1):
                            ny, nx = cy+dy, cx+dx
                            if 0 <= ny < h and 0 <= nx < w and bits[ny, nx] and not seen[ny, nx]:
                                seen[ny, nx] = True; q.append((ny, nx))
    return n
