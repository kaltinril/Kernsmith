#!/usr/bin/env python3
"""
Build a before / after / diff HTML page from a regression_check.py run.

regression_check.py leaves three files per changed atlas in the output directory: the base
side (main_<name>), the branch side (<name>) and the magenta diff (diff_<name>). This script
reads the run's log, finds every atlas page and .fnt file the diff step reported DIFFERENT,
and writes one page that shows the three images next to each other, with zoom and
background controls, plus a summary of the kerning pairs that changed.

Usage:
    python tests/bmfont-compare/regression_check.py --base <sha> --tolerance 1 > run.log
    python tests/bmfont-compare/before_after_page.py run.log \
        --base-label v0.27.0 --current-label "main 591b5fc"

    # Also copy the page and only the images it uses into a folder that can be shared
    python tests/bmfont-compare/before_after_page.py run.log --copy-to reports/my-run

The page is written into the output directory next to the images it references. The next
harness run does not delete it, but it does replace those images, so use --copy-to for
anything that has to outlive the run.
"""

import argparse
import collections
import html
import os
import re
import shutil
import sys

BACKENDS = ["freetype", "native", "stbtruetype", "gdi", "directwrite"]
BASELINE_PREFIX = "main_"
DIFF_PREFIX = "diff_"
PAGE_NAME = "before-after.html"
MAX_KERNING_ROWS = 40

_ATLAS = re.compile(r"\s*(.+)-(%s)_(\d+)\.png: DIFFERENT \((\d+) pixels, ([\d.]+)%%\)" % "|".join(BACKENDS))
_FNT = re.compile(r"\s*(.+)-(%s)\.fnt: DIFFERENT" % "|".join(BACKENDS))
_NEW = re.compile(r"\s*NEW config '([^']+)'")
_KERNING = re.compile(r"kerning first=(\d+) second=(\d+) amount=(-?\d+)")
_SIZED = re.compile(r"^(.*?)-(\d+)-o(\d+)$")

STYLE = """
:root{--bg:#1b1d22;--fg:#e6e6e6;--mut:#9aa0aa;--card:#24272e;--line:#3a3f4a;--chk1:#3a3d45;--chk2:#2c2f36}
body{background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,Segoe UI,sans-serif;margin:0;padding:16px 20px 60px}
h1{font-size:20px;margin:0 0 4px} h2{font-size:17px;margin:34px 0 4px;border-top:1px solid var(--line);padding-top:18px}
h3{font-size:13px;margin:14px 0 6px;color:var(--mut);font-weight:600;text-transform:uppercase;letter-spacing:.04em}
p,li{max-width:900px} .mut{color:var(--mut)}
.bar{position:sticky;top:0;background:var(--bg);padding:8px 0;border-bottom:1px solid var(--line);z-index:5;display:flex;gap:14px;flex-wrap:wrap;align-items:center}
button{background:var(--card);color:var(--fg);border:1px solid var(--line);border-radius:4px;padding:4px 10px;cursor:pointer}
button.on{background:#3b6fd4;border-color:#3b6fd4}
.row{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-bottom:14px}
.row.one{grid-template-columns:1fr}
.cell{background:var(--card);border:1px solid var(--line);border-radius:4px;overflow:auto;max-height:560px}
.cell .cap{position:sticky;left:0;top:0;background:var(--card);padding:3px 8px;font-size:12px;color:var(--mut);border-bottom:1px solid var(--line)}
.cell img{display:block;image-rendering:pixelated;width:calc(var(--w)*var(--z));max-width:none}
body.chk .cell .imgw{background:repeating-conic-gradient(var(--chk1) 0 25%,var(--chk2) 0 50%) 0 0/16px 16px}
body.white .cell .imgw{background:#fff} body.blue .cell .imgw{background:#4a6fb0}
.imgw{width:max-content}
table{border-collapse:collapse;margin:6px 0 12px} td,th{border:1px solid var(--line);padding:2px 9px;text-align:right} th{color:var(--mut);font-weight:600}
a{color:#7fb0ff} nav a{margin-right:10px;white-space:nowrap}
"""

SCRIPT = """
const zb=document.getElementById('zb'),bb=document.getElementById('bb');
function grp(el,opts,cur,fn){opts.forEach(([l,v])=>{const b=document.createElement('button');b.textContent=l;if(v===cur)b.className='on';b.onclick=()=>{[...el.children].forEach(c=>c.className='');b.className='on';fn(v)};el.appendChild(b);el.append(' ')})}
grp(zb,[['1x',1],['2x',2],['3x',3],['4x',4],['6x',6],['8x',8]],4,v=>document.body.style.setProperty('--z',v));
grp(bb,[['blue','blue'],['checker','chk'],['white','white']],'blue',v=>document.body.className=v);
document.querySelectorAll('.row').forEach(r=>{const cs=[...r.querySelectorAll('.cell')];let lock=false;cs.forEach(c=>c.addEventListener('scroll',()=>{if(lock)return;lock=true;cs.forEach(o=>{if(o!==c){o.scrollTop=c.scrollTop;o.scrollLeft=c.scrollLeft}});lock=false}))});
"""


def parse_log(lines):
    """Return (changed atlases by config, changed .fnt (config, backend) pairs, new configs)."""
    atlases = collections.OrderedDict()
    fnts = []
    new_configs = []
    for line in lines:
        m = _ATLAS.match(line)
        if m:
            config, backend, page, pixels, percent = m.groups()
            atlases.setdefault(config, []).append((backend, int(page), int(pixels), percent))
            continue
        m = _FNT.match(line)
        if m:
            if (m[1], m[2]) not in fnts:
                fnts.append((m[1], m[2]))
            continue
        m = _NEW.match(line)
        if m and m[1] not in new_configs:
            new_configs.append(m[1])
    for rows in atlases.values():
        rows.sort(key=lambda row: (BACKENDS.index(row[0]), row[1]))
    return atlases, fnts, new_configs


def config_order(config):
    """Sort key: a '<name>-<size>-o<outline>' family reads by outline, then size, numerically."""
    m = _SIZED.match(config)
    if m:
        return (m[1], int(m[3]), int(m[2]))
    return (config, 0, 0)


def read_kerning(path):
    pairs = collections.OrderedDict()
    if os.path.exists(path):
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line in handle:
                m = _KERNING.match(line)
                if m:
                    pairs[(int(m[1]), int(m[2]))] = int(m[3])
    return pairs


def kerning_delta(before, after):
    """Return (changed, added, removed) pair keys, each in file order."""
    changed = [key for key in before if key in after and before[key] != after[key]]
    added = [key for key in after if key not in before]
    removed = [key for key in before if key not in after]
    return changed, added, removed


def _char(codepoint):
    return html.escape(chr(codepoint)) if 32 < codepoint < 127 else "U+%04X" % codepoint


def _image_width(path):
    from PIL import Image
    with Image.open(path) as image:
        return image.size[0]


def build_page(log_lines, out_dir, base_label, current_label):
    """Return (page html, list of image file names the page references)."""
    atlases, fnts, new_configs = parse_log(log_lines)
    used = []
    parts = []
    title = "KernSmith: %s (before) vs %s (after)" % (base_label, current_label)
    parts.append('<!doctype html><meta charset="utf-8"><title>%s</title><style>%s</style>'
                 % (html.escape(title), STYLE))
    parts.append('<body class="blue" style="--z:4"><h1>%s</h1>' % html.escape(title))
    parts.append('<p class="mut">Regression harness output. Left = %s atlas, middle = %s, right = harness diff '
                 '(magenta = changed pixels). Each cell scrolls; the three cells in a row scroll together.</p>'
                 % (html.escape(base_label), html.escape(current_label)))
    parts.append('<div class="bar"><span>Zoom</span><span id="zb"></span><span>Background</span>'
                 '<span id="bb"></span></div><nav style="margin-top:10px">')
    configs = sorted(atlases, key=config_order)
    for config in configs:
        parts.append('<a href="#%s">%s</a>' % (html.escape(config), html.escape(config)))
    if fnts:
        parts.append('<a href="#kerning">kerning (.fnt)</a>')
    if new_configs:
        parts.append('<a href="#new">new configs</a>')
    parts.append('</nav>')
    if not configs:
        parts.append('<p>No atlas PNG differs.</p>')

    for config in configs:
        parts.append('<h2 id="%s">%s</h2>' % (html.escape(config), html.escape(config)))
        for backend, page, pixels, percent in atlases[config]:
            name = "%s-%s_%d.png" % (config, backend, page)
            current = os.path.join(out_dir, name)
            width = _image_width(current) if os.path.exists(current) else 256
            parts.append('<h3>%s, page %d &mdash; %s pixels differ (%s%%)</h3><div class="row" style="--w:%dpx">'
                         % (backend, page, format(pixels, ","), percent, width))
            for caption, file_name in (("before: " + base_label, BASELINE_PREFIX + name),
                                       ("after: " + current_label, name),
                                       ("diff", DIFF_PREFIX + name)):
                if os.path.exists(os.path.join(out_dir, file_name)):
                    used.append(file_name)
                    parts.append('<div class="cell"><div class="cap">%s</div><div class="imgw">'
                                 '<img loading="lazy" src="%s"></div></div>'
                                 % (html.escape(caption), html.escape(file_name)))
                else:
                    parts.append('<div class="cell"><div class="cap">%s (missing)</div></div>' % html.escape(caption))
            parts.append('</div>')

    if fnts:
        parts.append('<h2 id="kerning">Kerning changes (.fnt)</h2>')
        for config, backend in fnts:
            name = "%s-%s.fnt" % (config, backend)
            before = read_kerning(os.path.join(out_dir, BASELINE_PREFIX + name))
            after = read_kerning(os.path.join(out_dir, name))
            changed, added, removed = kerning_delta(before, after)
            parts.append('<h3>%s, %s</h3><p>%d pairs before, %d after: %d removed, %d added, %d changed value, '
                         '%d unchanged.</p>' % (html.escape(config), backend, len(before), len(after), len(removed),
                                                len(added), len(changed), len(before) - len(removed) - len(changed)))
            keys = changed + added + removed
            if keys:
                parts.append('<table><tr><th>pair</th><th>before</th><th>after</th></tr>')
                for key in keys[:MAX_KERNING_ROWS]:
                    parts.append('<tr><td>%s %s</td><td>%s</td><td>%s</td></tr>'
                                 % (_char(key[0]), _char(key[1]), before.get(key, ""), after.get(key, "")))
                parts.append('</table><p class="mut">First %d of %d differing pairs.</p>'
                             % (min(len(keys), MAX_KERNING_ROWS), len(keys)))
            else:
                parts.append('<p class="mut">No kerning pair differs; the difference is in other lines.</p>')

    if new_configs:
        parts.append('<h2 id="new">New configs (no %s baseline)</h2>' % html.escape(base_label))
        for config in new_configs:
            file_name = "comparison-%s.png" % config
            path = os.path.join(out_dir, file_name)
            if not os.path.exists(path):
                parts.append('<p>%s: no comparison image.</p>' % html.escape(config))
                continue
            used.append(file_name)
            parts.append('<h3>%s</h3><div class="row one" style="--w:%dpx"><div class="cell"><div class="cap">after: %s, '
                         'all backends</div><div class="imgw"><img loading="lazy" src="%s"></div></div></div>'
                         % (html.escape(config), _image_width(path), html.escape(current_label), html.escape(file_name)))

    parts.append('<script>%s</script>' % SCRIPT)
    return "\n".join(parts), used


def main(argv=None):
    harness_dir = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description="Build a before/after/diff page from a regression_check.py log.")
    parser.add_argument("log", help="Captured stdout of regression_check.py (or diff_comparisons.py)")
    parser.add_argument("--dir", default=os.path.join(harness_dir, "output"),
                        help="Harness output directory (default: tests/bmfont-compare/output)")
    parser.add_argument("--base-label", default="base", help="Name shown for the baseline side")
    parser.add_argument("--current-label", default="branch", help="Name shown for the current side")
    parser.add_argument("--copy-to", help="Also copy the page and the images it uses into this directory")
    args = parser.parse_args(argv)

    with open(args.log, encoding="utf-8", errors="replace") as handle:
        page, used = build_page(handle.readlines(), args.dir, args.base_label, args.current_label)

    target = os.path.join(args.dir, PAGE_NAME)
    with open(target, "w", encoding="utf-8") as handle:
        handle.write(page)
    print("Wrote %s (%d images referenced)" % (target, len(used)))

    if args.copy_to:
        os.makedirs(args.copy_to, exist_ok=True)
        shutil.copyfile(target, os.path.join(args.copy_to, PAGE_NAME))
        for file_name in used:
            shutil.copyfile(os.path.join(args.dir, file_name), os.path.join(args.copy_to, file_name))
        print("Copied the page and %d images to %s" % (len(used), args.copy_to))
    return 0


if __name__ == "__main__":
    sys.exit(main())
