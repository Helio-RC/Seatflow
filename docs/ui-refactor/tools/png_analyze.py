#!/usr/bin/env python3
"""纯标准库 PNG 解码 + 控件定位工具（Avalonia WASM 的 canvas 界面无 DOM，
需通过像素分析定位按钮/导航项中心坐标）。

用法:
    python3 png_analyze.py <shot.png> buttons   # 找对话框按钮填充块
    python3 png_analyze.py <shot.png> text      # 找底部深色文字簇

注意: 本环境截图链路会把纯白压到 242（最大值），阈值已按此校准。
"""
import struct, zlib, sys


def read_png(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', 'not png'
    pos = 8
    idat = b''
    w = h = bd = ct = None
    while pos < len(data):
        ln = struct.unpack('>I', data[pos:pos+4])[0]
        typ = data[pos+4:pos+8]
        chunk = data[pos+8:pos+8+ln]
        pos += 12 + ln
        if typ == b'IHDR':
            w, h, bd, ct, comp, filt, inter = struct.unpack('>IIBBBBB', chunk)
        elif typ == b'IDAT':
            idat += chunk
        elif typ == b'IEND':
            break
    raw = zlib.decompress(idat)
    bpp = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ct]
    stride = w * bpp
    out = bytearray(h * stride)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]; p += 1
        line = bytearray(raw[p:p+stride]); p += stride
        if f == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i-bpp]) & 0xff
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xff
        elif f == 3:
            for i in range(stride):
                a = line[i-bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xff
        elif f == 4:
            for i in range(stride):
                a = line[i-bpp] if i >= bpp else 0
                b = prev[i]
                c = prev[i-bpp] if i >= bpp else 0
                pa, pb, pc = abs(b-c), abs(a-c), abs(a+b-2*c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xff
        out[y*stride:(y+1)*stride] = line
        prev = line
    return w, h, bpp, bytes(out)


def analyze(path, what='buttons'):
    w, h, bpp, buf = read_png(path)
    print(f'size: {w}x{h} bpp={bpp}')

    def px(x, y):
        o = (y*w + x) * bpp
        return buf[o], buf[o+1], buf[o+2]

    # 对话框（近白 >=235）
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(0, h):
        for x in range(0, w):
            r, g, b = px(x, y)
            if r >= 235 and g >= 235 and b >= 235:
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
    print(f'dialog bbox: x[{minx},{maxx}] y[{miny},{maxy}]')
    if maxx < 0:
        return

    band_top = max(maxy - 110, miny)
    cols = {}
    for y in range(band_top, maxy + 1):
        for x in range(minx, maxx + 1):
            r, g, b = px(x, y)
            if what == 'buttons':
                hit = 160 <= r <= 228 and abs(r-g) <= 6 and abs(g-b) <= 6
            else:
                hit = r < 150 and g < 150 and b < 150
            if hit:
                cols.setdefault(x, []).append(y)
    xs = sorted(cols)
    clusters = []
    cur = []
    gap = 16 if what == 'buttons' else 12
    for x in xs:
        if cur and x - cur[-1] > gap:
            clusters.append(cur); cur = []
        cur.append(x)
    if cur:
        clusters.append(cur)
    for c in clusters:
        ys = [y for x in c for y in cols[x]]
        cx = (c[0] + c[-1]) / 2
        cy = (min(ys) + max(ys)) / 2
        print(f'{what} x[{c[0]},{c[-1]}] y[{min(ys)},{max(ys)}] center=({cx:.0f},{cy:.0f})')


if __name__ == '__main__':
    analyze(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else 'buttons')
