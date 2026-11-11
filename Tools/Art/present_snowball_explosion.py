"""Encode cannon -> snowball explosion frame sequences into review-ready previews.

Usage:
    python Tools/Art/present_snowball_explosion.py PROJECT_ROOT --ffmpeg PATH [--only CLIP_ID]

Input (written by the Unity editor capture tool):
    docs/art/cannon-snowball-explosion/frames/<clip-id>/frame_0000.png ...
    docs/art/cannon-snowball-explosion/render-report.txt   (optional per-clip fps table)
    docs/art/cannon-snowball-explosion/<clip-id>-still.png (optional review stills)

Output (next to the input, under docs/art/cannon-snowball-explosion/):
    <clip-id>.mp4          H.264 / yuv420p / CFR / +faststart       (ffmpeg)
    <clip-id>.gif          palette-optimized looping preview        (Pillow)
    <clip-id>-sheet.jpg    labelled contact sheet of evenly spaced frames
    00-comparison.jpg      multi-clip still comparison with Chinese captions
    encode-report.txt      paths, frame counts, fps, resolutions and byte sizes

Optional per-clip metadata (UTF-8 JSON, any subset of keys):
    docs/art/cannon-snowball-explosion/clips.json   renderer manifest (id/label/style/fps/...)
    frames/<clip-id>/clip.json        {"title": "...", "caption": "...", "fps": 30}
    frames/<clip-id>/meta.json        same keys (clip.json wins)
    frames/clip-labels.json           {"<clip-id>": {"title": "...", ...}}
    docs/art/cannon-snowball-explosion/clip-labels.json   same format
Precedence: render-report.txt < clips.json < clip-labels.json/clip.json.
Optional hand-picked stills for 00-comparison.jpg:
    docs/art/cannon-snowball-explosion/stills/<clip-id>/*.png
Otherwise the comparison uses <clip-id>-still*.png, then the middle rendered frame.
The comparison board keeps only clips that have a real still when at least two do.

Dependencies: Python 3.12 + Pillow; ffmpeg only for the mp4 (auto-detected when
--ffmpeg is omitted). No GUI, no network, no pip installs. The whole run is
deterministic: sorted clip/frame order, fixed layouts/fonts and fixed ffmpeg flags.
"""

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# --- palette / typography --------------------------------------------------
BG = "#102335"
FG = "#edf5fa"
SUB = "#b2cada"
ACCENT = "#ffc38c"
DIM = "#7d99ad"

FONT_CANDIDATES = [
    "C:/Windows/Fonts/msyh.ttc",
    "C:/Windows/Fonts/msyhbd.ttc",
    "C:/Windows/Fonts/simhei.ttf",
    "C:/Windows/Fonts/simsun.ttc",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
]
BOLD_CANDIDATES = [
    "C:/Windows/Fonts/msyhbd.ttc",
    "C:/Windows/Fonts/msyh.ttc",
    "C:/Windows/Fonts/simhei.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
]
FFMPEG_FALLBACKS = [
    "D:/Tools/VideoTools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v9.0.2.exe",
    "D:/Tools/VideoTools/imageio_ffmpeg/binaries/ffmpeg.exe",
    "C:/ffmpeg/bin/ffmpeg.exe",
]

GIF_WIDTH = 960
GIF_BUDGET = 9_000_000  # fall back to fewer frames if the gif gets too heavy
GIF_FPS_CAP = 30.0
GIF_MIN_SECONDS = 2.0  # review gifs are stretched to this length when short
GIF_MAX_SECONDS = 4.0  # and sampled down when longer
DEFAULT_FPS = 60.0
SHEET_FRAMES = 8
SHEET_COLS = 4
SHEET_TILE_W = 440
MARGIN = 44
COMPARISON_W = 1920
COMPARISON_TITLE = "大炮轰击雪球爆炸  /  多片段静帧对比"
DEFAULT_CAPTION = "炮击雪球爆炸 · 关键帧时间轴"
# Chinese labels for the style ids written by the Unity preview renderer.
STYLE_LABELS = {
    "current": ("现状", "现状：炮弹命中后雪球瞬间消失，作为对照"),
    "SnowPowder": ("A 雪雾爆裂", "雪雾爆裂：雪球化成扬起的雪雾，向外扩散后缓缓落定"),
    "IceShatter": ("B 冰晶碎裂", "冰晶碎裂：雪球碎成冰晶与雪块，沿冲击方向飞散"),
    "AvalancheBlast": ("C 雪崩冲击", "雪崩冲击：雪雾夹带冲击环向四周推开，压迫感最强"),
}


def first_file(candidates):
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return Path(candidate)
    return None


FONT_PATH = first_file(FONT_CANDIDATES)
FONT_BOLD_PATH = first_file(BOLD_CANDIDATES) or FONT_PATH
_fonts = {}


def font(size, bold=False):
    key = (int(size), bool(bold))
    if key not in _fonts:
        path = FONT_BOLD_PATH if bold else FONT_PATH
        try:
            _fonts[key] = ImageFont.truetype(str(path), int(size)) if path else ImageFont.load_default()
        except OSError:
            _fonts[key] = ImageFont.load_default()
    return _fonts[key]


def text(canvas, xy, value, size=28, color=FG, bold=False):
    ImageDraw.Draw(canvas).text(xy, value, font=font(size, bold), fill=color)


def fit(image, box_w, box_h):
    picture = image.copy()
    picture.thumbnail((max(1, box_w), max(1, box_h)), Image.Resampling.LANCZOS)
    return picture


# --- small helpers ---------------------------------------------------------
def natural_key(path):
    return [int(part) if part.isdigit() else part.lower() for part in re.split(r"(\d+)", path.name)]


def number(value):
    """Format a float without useless decimals (30.0 -> '30', 29.97 -> '29.97')."""
    return f"{float(value):g}"


def duration_text(frames, fps):
    return f"{len(frames) / fps:.3f} s"


def human_size(size):
    if size >= 1024 * 1024:
        return f"{size / (1024 * 1024):.2f} MB"
    if size >= 1024:
        return f"{size / 1024:.0f} KB"
    return f"{size} B"


def relative(path, root):
    try:
        return Path(path).resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return Path(path).as_posix()


def evenly_spaced(total, count):
    """Deterministic evenly spaced indices, endpoints included, no duplicates."""
    if count >= total:
        return list(range(total))
    if count <= 1:
        return [total // 2]
    step = (total - 1) / (count - 1)
    return sorted({int(round(index * step)) for index in range(count)})


def list_pngs(directory):
    if not directory.is_dir():
        return []
    return sorted(
        (path for path in directory.iterdir() if path.is_file() and path.suffix.lower() == ".png"),
        key=natural_key,
    )


# --- clip discovery / metadata --------------------------------------------
def discover_clips(frames_root):
    clips = []
    if frames_root.is_dir():
        for child in sorted(frames_root.iterdir(), key=natural_key):
            if child.is_dir():
                frames = list_pngs(child)
                if frames:
                    clips.append((child.name, child, frames))
        if not clips:
            # Be forgiving: allow a flat frame folder without clip subfolders.
            frames = list_pngs(frames_root)
            if frames:
                clips.append(("preview", frames_root, frames))
    return clips


def load_metadata(frames_root, out_dir, clip_id, clip_dir):
    metadata = {}
    for path in (frames_root / "clip-labels.json", out_dir / "clip-labels.json"):
        if not path.is_file():
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        entry = data.get(clip_id) if isinstance(data, dict) else None
        if isinstance(entry, dict):
            metadata.update(entry)
    for name in ("clip.json", "meta.json"):
        path = clip_dir / name
        if path.is_file():
            try:
                data = json.loads(path.read_text(encoding="utf-8-sig"))
            except (OSError, ValueError):
                data = None
            if isinstance(data, dict):
                metadata.update(data)
    return metadata


def clip_texts(clip_id, metadata):
    title = str(metadata.get("title") or clip_id)
    caption = str(metadata.get("caption") or metadata.get("description") or DEFAULT_CAPTION)
    return title, caption


def load_render_report(out_dir):
    """Read the renderer's per-clip fps table when it exists.

    The Unity preview renderer writes docs/art/cannon-snowball-explosion/render-report.txt
    with a CSV block: clip,style,resolution,fps,preroll,effect_frames,duration_s
    Gameplay clips are 60 fps and closeups are rendered at 120 fps but encoded at 30 fps,
    so this table is the authoritative source for playback speed.
    """
    path = out_dir / "render-report.txt"
    result = {}
    if not path.is_file():
        return result
    try:
        lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    except OSError:
        return result
    for position, line in enumerate(lines):
        if not line.strip().lower().startswith("clip,style,resolution"):
            continue
        header = [cell.strip() for cell in line.split(",")]
        for row in lines[position + 1:]:
            cells = [cell.strip() for cell in row.split(",")]
            if len(cells) != len(header):
                break
            entry = dict(zip(header, cells))
            clip_id = entry.get("clip")
            if not clip_id:
                continue
            meta = {}
            try:
                meta["fps"] = float(entry.get("fps", ""))
            except ValueError:
                pass
            try:
                meta["preroll_frames"] = int(entry.get("preroll", ""))
            except ValueError:
                pass
            style = entry.get("style", "")
            label, caption = STYLE_LABELS.get(style, (style, ""))
            closeup = clip_id.lower().startswith("closeup")
            if style in STYLE_LABELS:
                meta["title"] = f"{label} · {'慢放近景' if closeup else '实机视角'}"
            if closeup and caption:
                caption += "（慢放近景，便于观察粒子与碎片）"
            meta["caption"] = caption or DEFAULT_CAPTION
            result[clip_id] = meta
        break
    return result


def load_clips_json(out_dir):
    """Read the renderer's clips.json manifest: [{"id", "label", "style", "camera", "fps", ...}]."""
    path = out_dir / "clips.json"
    result = {}
    if not path.is_file():
        return result
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return result
    entries = data.get("clips") if isinstance(data, dict) else data
    if not isinstance(entries, list):
        return result
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        clip_id = str(entry.get("id") or entry.get("clip") or "").strip()
        if not clip_id:
            continue
        meta = {}
        label = str(entry.get("label") or "").strip()
        closeup = str(entry.get("camera") or "").lower() == "closeup" or clip_id.lower().startswith("closeup")
        if label:
            meta["title"] = label + (" · 慢放近景" if closeup and "近景" not in label else "")
        try:
            meta["fps"] = float(entry.get("fps"))
        except (TypeError, ValueError):
            pass
        try:
            meta["preroll_frames"] = int(entry.get("preroll_frames"))
        except (TypeError, ValueError):
            pass
        style = str(entry.get("style") or "")
        caption = STYLE_LABELS.get(style, ("", ""))[1]
        if closeup and caption:
            caption += "（慢放近景，便于观察粒子与碎片）"
        if caption:
            meta["caption"] = caption
        result[clip_id] = meta
    return result


def merge_clip_metadata(*sources):
    merged = {}
    for source in sources:
        for clip_id, meta in source.items():
            merged.setdefault(clip_id, {}).update(meta)
    return merged


def ellipsize(canvas, value, size, max_width, bold=False):
    draw = ImageDraw.Draw(canvas)
    font_obj = font(size, bold)
    if draw.textlength(value, font=font_obj) <= max_width:
        return value
    while value:
        value = value[:-1]
        if draw.textlength(value + "…", font=font_obj) <= max_width:
            return value + "…"
    return "…"


def validate_frames(frames):
    sizes = []
    for path in frames:
        try:
            with Image.open(path) as image:
                sizes.append(image.size)
        except OSError as error:
            raise RuntimeError(f"cannot read frame {path.name}: {error}") from error
    unique = sorted(set(sizes))
    if len(unique) != 1:
        detail = ", ".join(f"{size[0]}x{size[1]}" for size in unique)
        raise RuntimeError(f"frame sizes differ inside one clip ({detail}); re-render at a single resolution")
    width, height = unique[0]
    if width < 2 or height < 2:
        raise RuntimeError(f"frame resolution {width}x{height} is too small")
    return width, height


# --- ffmpeg ---------------------------------------------------------------
def resolve_ffmpeg(value):
    candidates = [value] if value else []
    candidates += [os.environ.get("FFMPEG"), shutil.which("ffmpeg")] + FFMPEG_FALLBACKS
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return Path(candidate).resolve()
    return None


def contiguous_pattern(frames):
    """Return (prefix, suffix, zero_padding, first_number) when ffmpeg's image2
    pattern matches the sequence, else None."""
    matches = [re.match(r"^(.*?)(\d+)(\.[^.]+)$", frame.name) for frame in frames]
    if any(match is None for match in matches):
        return None
    prefixes = {match.group(1) for match in matches}
    suffixes = {match.group(3).lower() for match in matches}
    widths = {len(match.group(2)) for match in matches}
    if len(prefixes) != 1 or len(suffixes) != 1 or len(widths) != 1:
        return None
    numbers = [int(match.group(2)) for match in matches]
    if numbers != list(range(numbers[0], numbers[0] + len(numbers))):
        return None
    prefix, suffix = prefixes.pop(), suffixes.pop()
    return prefix, suffix, widths.pop(), numbers[0]


def format_error(error):
    if isinstance(error, subprocess.CalledProcessError):
        stderr = (error.stderr or "").strip()
        return stderr.splitlines()[-1] if stderr else f"exit code {error.returncode}"
    return str(error)


def encode_mp4(frames, out_path, ffmpeg, fps):
    """H.264 / yuv420p / constant frame rate / progressive-download faststart."""
    pattern = contiguous_pattern(frames)
    staging = None
    try:
        if pattern:
            prefix, suffix, width, start = pattern
            input_pattern = str(frames[0].parent / f"{prefix}%0{width}d{suffix}")
            sequence = ["-start_number", str(start)]
        else:
            # Non-contiguous names: stage sequential names (hardlinks when possible).
            staging = Path(tempfile.mkdtemp(prefix=".frames-", dir=str(out_path.parent)))
            for index, source in enumerate(frames):
                target = staging / f"frame_{index:04d}.png"
                try:
                    os.link(source, target)
                except OSError:
                    shutil.copyfile(source, target)
            input_pattern = str(staging / "frame_%04d.png")
            sequence = []
        command = [
            str(ffmpeg), "-y", "-hide_banner", "-loglevel", "error",
            "-framerate", number(fps), *sequence, "-i", input_pattern,
            "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
            "-c:v", "libx264", "-preset", "medium", "-crf", "18",
            "-pix_fmt", "yuv420p", "-r", number(fps),
            "-movflags", "+faststart", "-an", str(out_path),
        ]
        subprocess.run(command, check=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
    finally:
        if staging is not None:
            shutil.rmtree(staging, ignore_errors=True)


def validate_mp4(path, ffmpeg):
    probe = subprocess.run(
        [str(ffmpeg), "-hide_banner", "-i", str(path)],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    info = probe.stderr or ""
    missing = [token for token in ("Video: h264", "yuv420p") if token not in info]
    if missing:
        raise RuntimeError(f"encoded mp4 is missing {' / '.join(missing)}: {info.strip().splitlines()[-1] if info.strip() else 'no stream info'}")
    decode = subprocess.run(
        [str(ffmpeg), "-v", "error", "-i", str(path), "-f", "null", "-"],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    if decode.returncode != 0:
        raise RuntimeError(f"encoded mp4 does not decode: {(decode.stderr or '').strip()}")
    match = re.search(r"Video: h264.*?, (\d+)x(\d+)", info)
    if match is None:
        raise RuntimeError("could not read the encoded mp4 resolution")
    return (int(match.group(1)), int(match.group(2)))


# --- gif ------------------------------------------------------------------
def encode_gif(frames, out_path, fps, width_cap=GIF_WIDTH, gif_fps=0.0, budget=GIF_BUDGET):
    """Palette-optimized looping gif, <= width_cap wide, <= gif fps cap, 2-4 s.

    All source frames are kept when possible: a 60 fps gameplay clip therefore
    plays at half speed in the gif, which is easier to review than 30 fps sampling.
    Short sequences are stretched to GIF_MIN_SECONDS, long ones sampled to
    GIF_MAX_SECONDS; the budget fallback then drops frames without losing duration.
    """
    with Image.open(frames[0]) as first:
        source_w, source_h = first.size
    target_w = max(2, min(width_cap, source_w))
    target_h = max(2, round(source_h * target_w / source_w))

    rate = min(gif_fps if gif_fps > 0 else GIF_FPS_CAP, fps)
    picked = list(range(len(frames)))
    seconds = len(picked) / rate
    if seconds > GIF_MAX_SECONDS:
        count = max(2, int(math.floor(GIF_MAX_SECONDS * rate)))
        picked = evenly_spaced(len(frames), count)
        seconds = len(picked) / rate
    elif seconds < GIF_MIN_SECONDS:
        seconds = GIF_MIN_SECONDS
    boundaries = [int(round(index * seconds * 100 / len(picked))) for index in range(len(picked) + 1)]
    durations = [max(1, boundaries[index + 1] - boundaries[index]) * 10 for index in range(len(picked))]
    expected_ms = boundaries[-1] * 10

    images = []
    for index in picked:
        with Image.open(frames[index]) as image:
            images.append(image.convert("RGB").resize((target_w, target_h), Image.Resampling.LANCZOS))

    def build_palette(source_images):
        sample = evenly_spaced(len(source_images), min(16, len(source_images)))
        sheet = Image.new("RGB", (target_w, target_h * len(sample)))
        for row, index in enumerate(sample):
            sheet.paste(source_images[index], (0, row * target_h))
        return sheet.quantize(colors=256, method=Image.Quantize.MEDIANCUT)

    def save(source_images, source_durations):
        palette = build_palette(source_images)
        indexed = [image.quantize(palette=palette, dither=Image.Dither.NONE) for image in source_images]
        indexed[0].save(
            out_path, save_all=True, append_images=indexed[1:],
            duration=source_durations, loop=0, optimize=True,
        )

    save(images, durations)
    while out_path.stat().st_size > budget and len(images) > 8:
        # Deterministic budget fallback: keep every other frame, double its duration.
        images = images[::2]
        durations = [sum(durations[index:index + 2]) for index in range(0, len(durations), 2)]
        save(images, durations)

    frames_written, total_ms, loop = validate_gif(out_path, expected_ms, len(images))
    return {
        "frames": frames_written,  # Pillow merges pixel-identical frames, keeping total duration
        "source_frames": len(images),
        "fps": len(images) / (total_ms / 1000.0),  # intended playback rate
        "seconds": total_ms / 1000.0,
        "width": target_w,
        "height": target_h,
        "bytes": out_path.stat().st_size,
    }


def read_gif_metrics(path):
    with Image.open(path) as image:
        frames = getattr(image, "n_frames", 1)
        total_ms = 0
        for index in range(frames):
            image.seek(index)
            total_ms += image.info.get("duration") or 0
        loop = image.info.get("loop")
    return frames, total_ms, loop


def validate_gif(path, expected_ms, expected_source_frames):
    frames, total_ms, loop = read_gif_metrics(path)
    if frames < 1:
        raise RuntimeError("gif has no frames")
    if frames > expected_source_frames:
        raise RuntimeError(f"gif has {frames} frames, more than the {expected_source_frames} source frames")
    if expected_source_frames > 1 and loop != 0:
        raise RuntimeError(f"gif is not set to loop forever (loop={loop!r})")
    tolerance = max(80, expected_ms * 0.05)
    if abs(total_ms - expected_ms) > tolerance:
        raise RuntimeError(f"gif duration {total_ms} ms does not match the source ({expected_ms} ms)")
    return frames, total_ms, loop


# --- contact sheet --------------------------------------------------------
def sheet_indices(total, count, preroll=None):
    """Evenly spaced sheet frames that always include the impact (preroll) frame."""
    indices = evenly_spaced(total, count)
    if preroll is None or not 0 <= preroll < total or preroll in indices:
        return indices
    nearest = min(range(len(indices)), key=lambda slot: abs(indices[slot] - preroll))
    indices[nearest] = preroll
    return sorted(indices)


def build_sheet(frames, out_path, clip_id, title, caption, fps, count, width, height, preroll=None):
    indices = sheet_indices(len(frames), min(count, len(frames)), preroll)
    count = len(indices)
    rows = math.ceil(count / SHEET_COLS)
    tile_w = SHEET_TILE_W
    tile_h = max(1, round(tile_w * height / width))
    tile_h = min(tile_h, 330)
    gap = 16
    label_h = 32
    row_gap = 18
    header_h = 150
    footer_h = 74
    board_w = MARGIN * 2 + SHEET_COLS * tile_w + (SHEET_COLS - 1) * gap
    board_h = header_h + rows * (label_h + tile_h) + (rows - 1) * row_gap + footer_h
    board = Image.new("RGB", (board_w, board_h), BG)

    text(board, (MARGIN, 22), title, 40, ACCENT, bold=True)
    text(board, (MARGIN, 82), f"{clip_id} · {len(frames)} 帧 · {duration_text(frames, fps)} · {number(fps)} fps · {width}x{height}", 23, SUB)
    text(board, (MARGIN, 114), caption, 23, FG)

    for slot, index in enumerate(indices):
        column, row = slot % SHEET_COLS, slot // SHEET_COLS
        x = MARGIN + column * (tile_w + gap)
        y = header_h + row * (label_h + tile_h + row_gap)
        label = f"帧 {index:04d}   {index / fps:.3f} s" + ("   起爆" if preroll is not None and index == preroll else "")
        text(board, (x, y), label, 21, SUB)
        with Image.open(frames[index]) as image:
            board.paste(fit(image.convert("RGB"), tile_w, tile_h), (x, y + label_h))

    sampled = "（含起爆帧）" if preroll is not None and preroll in indices else ""
    footer = f"共 {len(frames)} 帧 · 取 {count} 个均匀采样帧{sampled} · 时间标签为片段内时间 · MP4 / GIF 展示完整时序"
    text(board, (MARGIN, board_h - 50), footer, 20, DIM)
    board.save(out_path, quality=93)
    return board.width, board.height, count


# --- comparison -----------------------------------------------------------
def representative_still(record, out_dir):
    """Best single still for the comparison board: hand-picked, renderer still,
    else the middle rendered frame. Returns (path, source)."""
    clip_id = record["clip_id"]
    stills_dir = out_dir / "stills" / clip_id
    if stills_dir.is_dir():
        files = sorted(
            (path for path in stills_dir.iterdir() if path.is_file() and path.suffix.lower() in {".png", ".jpg", ".jpeg"}),
            key=natural_key,
        )
        if files:
            return files[evenly_spaced(len(files), 1)[0]], "hand-picked"
    candidates = sorted(
        (path for path in out_dir.glob(f"{clip_id}-still*.png") if path.is_file()),
        key=natural_key,
    )
    if candidates:
        return candidates[evenly_spaced(len(candidates), 1)[0]], "renderer"
    frames = record["frames"]
    return frames[evenly_spaced(len(frames), 1)[0]], "frame"


def build_comparison(records, out_path, out_dir):
    """Uniform grid: one labelled still per clip, Chinese captions.

    When at least two clips have a real still (renderer *-still.png or a hand-picked
    still), only those clips are kept: in the real render that is the four
    gameplay hero stills (current + A/B/C) with their labels.
    """
    if not records:
        return None
    resolved = [(record, *representative_still(record, out_dir)) for record in records]
    with_still = [item for item in resolved if item[2] != "frame"]
    if len(with_still) >= 2:
        resolved = with_still
    count = len(resolved)
    cols = 2 if count <= 4 else 3
    rows = math.ceil(count / cols)
    gap = 20
    available = COMPARISON_W - MARGIN * 2
    tile_w = (available - (cols - 1) * gap) // cols
    tile_h = max(1, round(tile_w * 9 / 16))
    title_h, meta_h, image_gap, caption_h = 34, 26, 8, 36
    block_h = title_h + meta_h + image_gap + tile_h + caption_h
    row_gap = 22
    header_h, footer_h = 132, 84
    board_h = header_h + rows * block_h + (rows - 1) * row_gap + footer_h
    board = Image.new("RGB", (COMPARISON_W, board_h), BG)
    text(board, (MARGIN, 24), COMPARISON_TITLE, 44, ACCENT, bold=True)
    text(board, (MARGIN, 88), "同一比赛场景 · 同一炮弹与雪球资产 · 关键静帧对照 · 完整动效见各片段 MP4 / GIF", 23, SUB)

    still_count = 0
    for slot, (record, still, source) in enumerate(resolved):
        if source != "frame":
            still_count += 1
        column, row = slot % cols, slot // cols
        x = MARGIN + column * (tile_w + gap)
        y = header_h + row * (block_h + row_gap)
        text(board, (x, y), ellipsize(board, record["title"], 26, tile_w, bold=True), 26, ACCENT, bold=True)
        meta_line = f"{record['clip_id']} · {record['width']}x{record['height']} · {number(record['fps'])} fps"
        text(board, (x, y + title_h), ellipsize(board, meta_line, 19, tile_w), 19, SUB)
        image_top = y + title_h + meta_h + image_gap
        with Image.open(still) as image:
            picture = fit(image.convert("RGB"), tile_w, tile_h)
        board.paste(picture, (x + (tile_w - picture.width) // 2, image_top + (tile_h - picture.height) // 2))
        text(board, (x, image_top + tile_h + 8), ellipsize(board, record["caption"], 20, tile_w), 20, FG)

    footer = "静止帧：Unity 渲染器输出的 *-still.png；无静帧的片段取序列中段帧 · 中文说明来自渲染报告或片段元数据"
    text(board, (MARGIN, board.height - 46), footer, 20, DIM)
    board.save(out_path, quality=93)
    return board.width, board.height, still_count, count


# --- report ---------------------------------------------------------------
def write_report(root, frames_root, out_dir, records, errors, ffmpeg, args):
    render_report = out_dir / "render-report.txt"
    render_note = "per-clip fps" if render_report.is_file() else "missing; default fps"
    lines = [
        "Snowball explosion preview encode report",
        "=" * 44,
        "tool      : Tools/Art/present_snowball_explosion.py",
        f"project   : {root.as_posix()}",
        f"frames    : {relative(frames_root, root)}",
        f"render    : {relative(render_report, root)}  ({render_note})",
        f"ffmpeg    : {ffmpeg.as_posix() if ffmpeg else 'NOT FOUND'}",
        f"settings  : fps={number(args.fps)}  gif_width={args.gif_width}  "
        f"gif_fps<={number(GIF_FPS_CAP)}  gif_seconds={GIF_MIN_SECONDS:g}-{GIF_MAX_SECONDS:g}  "
        f"sheet_frames={args.sheet_frames}",
        "",
    ]
    total = 0
    for record in records:
        fps = record["fps"]
        lines.append(record["clip_id"])
        lines.append(f"  frames     : {relative(record['frames'][0], root)} .. {relative(record['frames'][-1], root)}"
                     f" ({len(record['frames'])} png)")
        lines.append(f"  resolution : {record['width']}x{record['height']}")
        lines.append(f"  fps        : {number(fps)}   duration: {duration_text(record['frames'], fps)}")
        mp4_path, mp4_bytes, mp4_res = record["mp4"]
        lines.append(f"  mp4        : {relative(mp4_path, root)}  bytes={mp4_bytes}  "
                     f"(H.264 yuv420p, {len(record['frames'])} frames, {mp4_res[0]}x{mp4_res[1]})")
        gif_path, gif_info = record["gif"]
        merged = "" if gif_info["frames"] >= gif_info["source_frames"] else f", {gif_info['source_frames']} source frames"
        lines.append(f"  gif        : {relative(gif_path, root)}  bytes={gif_info['bytes']}  "
                     f"({gif_info['frames']} frames{merged}, {gif_info['fps']:.2f} fps, "
                     f"{gif_info['seconds']:.3f} s, {gif_info['width']}x{gif_info['height']}, loop forever)")
        sheet_path, sheet_bytes, sheet_size, sheet_tiles = record["sheet"]
        lines.append(f"  sheet      : {relative(sheet_path, root)}  bytes={sheet_bytes}  "
                     f"({sheet_size[0]}x{sheet_size[1]}, {sheet_tiles} tiles)")
        total += mp4_bytes + gif_info["bytes"] + sheet_bytes
        lines.append("")
    if records:
        comparison_path, comparison_bytes, comparison_size, still_count, used_clips = records[0]["comparison"]
        lines.append("comparison")
        lines.append(f"  jpg        : {relative(comparison_path, root)}  bytes={comparison_bytes}  "
                     f"({comparison_size[0]}x{comparison_size[1]}, {used_clips} clips, "
                     f"{still_count} renderer/hand-picked stills)")
        total += comparison_bytes
        lines.append("")
    if errors:
        lines.append("errors")
        for clip_id, message in errors:
            lines.append(f"  {clip_id}: {message}")
        lines.append("")
        lines.append(f"status: FAILED ({len(errors)} clip(s)); {len(records)} clip(s) encoded")
    else:
        lines.append(f"status: ok ({len(records)} clip(s))")
    lines.append(f"total output bytes: {total} ({human_size(total)})")
    out_dir.mkdir(parents=True, exist_ok=True)
    report_path = out_dir / "encode-report.txt"
    report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return report_path


# --- main pipeline --------------------------------------------------------
def encode_clip(clip_id, clip_dir, frames, out_dir, ffmpeg, args):
    width, height = validate_frames(frames)
    labels = load_metadata(args.frames_root, out_dir, clip_id, clip_dir)
    # Renderer report supplies the real playback fps; JSON labels override it.
    metadata = {**args.render_meta.get(clip_id, {}), **labels}
    title, caption = clip_texts(clip_id, metadata)
    fps = float(metadata.get("fps", args.fps))
    if fps <= 0:
        raise RuntimeError(f"invalid fps {fps!r} in clip metadata")
    preroll = metadata.get("preroll_frames")
    try:
        preroll = int(preroll) if preroll is not None else None
    except (TypeError, ValueError):
        preroll = None

    out_dir.mkdir(parents=True, exist_ok=True)

    mp4_path = out_dir / f"{clip_id}.mp4"
    encode_mp4(frames, mp4_path, ffmpeg, fps)
    mp4_bytes = mp4_path.stat().st_size
    if mp4_bytes <= 0:
        raise RuntimeError("encoded mp4 is empty")
    mp4_res = validate_mp4(mp4_path, ffmpeg)

    gif_path = out_dir / f"{clip_id}.gif"
    gif_info = encode_gif(frames, gif_path, fps, args.gif_width, args.gif_fps)

    sheet_path = out_dir / f"{clip_id}-sheet.jpg"
    sheet_w, sheet_h, tiles = build_sheet(
        frames, sheet_path, clip_id, title, caption, fps, args.sheet_frames, width, height, preroll,
    )

    record = {
        "clip_id": clip_id,
        "frames": frames,
        "fps": fps,
        "preroll": preroll,
        "width": width,
        "height": height,
        "title": title,
        "caption": caption,
        "mp4": (mp4_path, mp4_bytes, mp4_res),
        "gif": (gif_path, gif_info),
        "sheet": (sheet_path, sheet_path.stat().st_size, (sheet_w, sheet_h), tiles),
    }
    print(f"  {clip_id}: {len(frames)} frames {width}x{height} @ {number(fps)} fps")
    print(f"    mp4  : {mp4_path} ({human_size(mp4_bytes)})")
    print(f"    gif  : {gif_path} ({human_size(gif_info['bytes'])}, {gif_info['frames']} frames)")
    print(f"    sheet: {sheet_path} ({human_size(record['sheet'][1])}, {sheet_w}x{sheet_h})")
    return record


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Encode cannon -> snowball explosion PNG frame sequences into review previews.",
    )
    parser.add_argument("project", type=Path, help="Unity project root (contains docs/art/cannon-snowball-explosion/frames)")
    parser.add_argument("--ffmpeg", type=Path, help="ffmpeg executable used for the H.264 mp4")
    parser.add_argument("--only", action="append", default=[], metavar="CLIP_ID",
                        help="encode only this clip folder (repeatable or comma-separated)")
    parser.add_argument("--fps", type=float, default=DEFAULT_FPS, help=f"frame rate of the rendered sequence (default {number(DEFAULT_FPS)})")
    parser.add_argument("--gif-width", type=int, default=GIF_WIDTH, help=f"gif max width in pixels (default {GIF_WIDTH})")
    parser.add_argument("--gif-fps", type=float, default=0.0, help="gif frame rate cap (default: min(source fps, 30))")
    parser.add_argument("--sheet-frames", type=int, default=SHEET_FRAMES, help=f"contact-sheet tiles (default {SHEET_FRAMES})")
    parser.add_argument("--list", action="store_true", help="only list detected clips, do not encode")
    args = parser.parse_args(argv)

    if args.fps <= 0:
        parser.error("--fps must be positive")
    if args.gif_width < 160:
        parser.error("--gif-width must be at least 160")
    if args.gif_fps < 0:
        parser.error("--gif-fps cannot be negative")
    if args.sheet_frames < 2:
        parser.error("--sheet-frames must be at least 2")

    root = args.project.resolve()
    frames_root = root / "docs/art/cannon-snowball-explosion/frames"
    out_dir = root / "docs/art/cannon-snowball-explosion"
    args.frames_root = frames_root

    clips = discover_clips(frames_root)
    if not clips:
        print(f"error: no clip frame folders found under {frames_root}", file=sys.stderr)
        print("expected: docs/art/cannon-snowball-explosion/frames/<clip-id>/frame_0000.png ...", file=sys.stderr)
        return 2

    selected = []
    requested = [item.strip() for value in args.only for item in value.split(",") if item.strip()]
    if requested:
        by_id = {clip_id: (clip_id, clip_dir, frames) for clip_id, clip_dir, frames in clips}
        for name in requested:
            key = Path(name).name
            if key not in by_id:
                print(f"error: clip {name!r} not found; detected: {', '.join(clip_id for clip_id, _, _ in clips)}", file=sys.stderr)
                return 2
            selected.append(by_id[key])
    else:
        selected = clips

    if args.list:
        for clip_id, clip_dir, frames in selected:
            with Image.open(frames[0]) as image:
                width, height = image.size
            print(f"{clip_id}: {len(frames)} frames {width}x{height}")
        return 0

    ffmpeg = resolve_ffmpeg(args.ffmpeg)
    if ffmpeg is None:
        print("error: ffmpeg not found; pass --ffmpeg PATH (required for the H.264 mp4)", file=sys.stderr)
        return 2
    print(f"project  : {root}")
    print(f"frames   : {frames_root}")
    print(f"output   : {out_dir}")
    print(f"ffmpeg   : {ffmpeg}")
    clips_meta = load_clips_json(out_dir)
    report_meta = load_render_report(out_dir)
    # clips.json is the renderer manifest and wins over the plain CSV fallback.
    args.render_meta = merge_clip_metadata(report_meta, clips_meta)
    if args.render_meta:
        print(f"metadata : {len(args.render_meta)} clip entr(ies) "
              f"(clips.json={len(clips_meta)}, render-report.txt={len(report_meta)})")

    records, errors = [], []
    for clip_id, clip_dir, frames in selected:
        try:
            records.append(encode_clip(clip_id, clip_dir, frames, out_dir, ffmpeg, args))
        except Exception as error:  # noqa: BLE001 - report per-clip failures and continue
            message = format_error(error)
            errors.append((clip_id, message))
            print(f"  {clip_id}: FAILED - {message}", file=sys.stderr)

    if records:
        comparison_path = out_dir / "00-comparison.jpg"
        comparison_w, comparison_h, still_count, used_clips = build_comparison(records, comparison_path, out_dir)
        comparison_size = (comparison_w, comparison_h)
        comparison_bytes = comparison_path.stat().st_size
        for record in records:
            record["comparison"] = (comparison_path, comparison_bytes, comparison_size, still_count, used_clips)
        print(f"  comparison: {comparison_path} ({human_size(comparison_bytes)}, {comparison_w}x{comparison_h}, "
              f"{used_clips} clips, {still_count} renderer/hand-picked stills)")

    report_path = write_report(root, frames_root, out_dir, records, errors, ffmpeg, args)
    print(f"report   : {report_path}")

    if errors:
        print(f"done with {len(errors)} failure(s) and {len(records)} encoded clip(s)", file=sys.stderr)
        return 1
    print(f"done: {len(records)} clip(s) encoded")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
