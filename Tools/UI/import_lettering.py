"""Derive Unity UV metadata from the original lettering sheets without changing PNG pixels."""
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / 'ArtSource/UI/lettering-source.json'
ASSETS = ROOT / 'Assets/Resources/UI/Handwriting'


def main():
    source = json.loads(SPEC.read_text(encoding='utf-8'))
    glyphs, report = [], []
    for sheet in source['sheets']:
        path = ASSETS / (sheet['file'] + '.png')
        image = Image.open(path)
        if image.mode != 'RGBA':
            raise ValueError(f'{path}: expected true alpha transparency')
        width, height = image.size
        alpha = image.getchannel('A').point(lambda value: 255 if value > 32 else 0)
        if image.getchannel('A').getextrema()[0] == 255:
            raise ValueError(f'{path}: background is opaque')
        for index, key in enumerate(sheet['keys']):
            column, row = index % sheet['cols'], index // sheet['cols']
            cuts = sheet.get('rowColumnCuts', {}).get(str(row), [index / sheet['cols'] for index in range(sheet['cols'] + 1)])
            cell = (round(cuts[column] * width), round(row * height / sheet['rows']),
                    round(cuts[column + 1] * width), round((row + 1) * height / sheet['rows']))
            box = alpha.crop(cell).getbbox()
            if box is None:
                raise ValueError(f'{sheet["file"]}: empty cell for {key!r}')
            left, top, right, bottom = (box[0] + cell[0], box[1] + cell[1],
                                         box[2] + cell[0], box[3] + cell[1])
            if left <= cell[0] or top <= cell[1] or right >= cell[2] or bottom >= cell[3]:
                raise ValueError(f'{sheet["file"]}: ink crosses the cell boundary for {key!r}')
            left, top = max(cell[0], left - 2), max(cell[1], top - 2)
            right, bottom = min(cell[2], right + 2), min(cell[3], bottom + 2)
            glyphs.append({'key': key, 'sheet': sheet['file'], 'uv': {
                'x': left / width, 'y': (height - bottom) / height,
                'width': (right - left) / width, 'height': (bottom - top) / height},
                'aspect': (right - left) / (bottom - top)})
            report.append({'key': key, 'sheet': sheet['file'], 'box': [left, top, right, bottom]})
    if len({item['key'] for item in glyphs}) != len(glyphs):
        raise ValueError('Duplicate lettering keys')
    (ASSETS / 'Lettering.json').write_text(json.dumps({'glyphs': glyphs}, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'ArtSource/UI/lettering-bounds.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Imported {len(glyphs)} hand-drawn labels and glyphs from {len(source["sheets"])} original sheets.')


if __name__ == '__main__':
    main()
