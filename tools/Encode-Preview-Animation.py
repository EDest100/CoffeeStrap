# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""Losslessly package actual captured PNG frames as an animated PNG. Standard library only."""
import json
import struct
import zlib
from pathlib import Path

root = Path(__file__).resolve().parents[1] / '.build/verification'
recordings = json.loads((root / 'temporal-recordings.json').read_text(encoding='utf-8'))
signature = b'\x89PNG\r\n\x1a\n'

def read_chunks(path):
    data = path.read_bytes()
    assert data[:8] == signature
    offset, chunks = 8, []
    while offset < len(data):
        size = struct.unpack_from('>I', data, offset)[0]
        kind = data[offset + 4:offset + 8]
        payload = data[offset + 8:offset + 8 + size]
        chunks.append((kind, payload))
        offset += size + 12
    return chunks

def chunk(kind, payload):
    return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload))

for stream in dict.fromkeys(frame['stream'] for frame in recordings):
    frames = [frame for frame in recordings if frame['stream'] == stream]
    first = read_chunks(root / frames[0]['file'])
    header = next(payload for kind, payload in first if kind == b'IHDR')
    width, height = struct.unpack_from('>II', header)
    output = bytearray(signature + chunk(b'IHDR', header))
    for kind, payload in first:
        if kind not in (b'IHDR', b'IDAT', b'IEND'):
            output.extend(chunk(kind, payload))
    output.extend(chunk(b'acTL', struct.pack('>II', len(frames), 0)))
    sequence = 0
    for index, frame in enumerate(frames):
        current = read_chunks(root / frame['file'])
        assert next(payload for kind, payload in current if kind == b'IHDR') == header
        delay = frames[index + 1]['elapsedMilliseconds'] - frame['elapsedMilliseconds'] if index + 1 < len(frames) else 650
        delay = max(1, min(65535, delay))
        output.extend(chunk(b'fcTL', struct.pack('>IIIIIHHBB', sequence, width, height, 0, 0, delay, 1000, 0, 0)))
        sequence += 1
        for kind, payload in current:
            if kind != b'IDAT':
                continue
            if index == 0:
                output.extend(chunk(b'IDAT', payload))
            else:
                output.extend(chunk(b'fdAT', struct.pack('>I', sequence) + payload))
                sequence += 1
    output.extend(chunk(b'IEND', b''))
    destination = root / (stream + '.apng')
    destination.write_bytes(output)
    print(f'Encoded {len(frames)} production-rendered controlled-time frames: {destination}')
