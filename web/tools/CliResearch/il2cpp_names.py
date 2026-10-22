"""IL2CPP global-metadata.dat에서 문자열 리터럴 표와 식별자(타입·메서드·필드 이름) 표를 뽑는다. 게임 파일은 읽기만 한다.

헤더(v31): sanity, version, stringLiteral(off,size), stringLiteralData(off,size), string(off,size), ...
- 문자열 리터럴: (uint32 length, int32 dataIndex) 표 + 데이터
- 식별자: NUL로 끝나는 UTF-8 문자열이 이어진 영역(정의 순서대로라 타입 이름 뒤에 그 필드·메서드 이름이 이어지는 경향)
사용: python il2cpp_names.py <global-metadata.dat> <출력 폴더>
"""
import json
import os
import struct
import sys

path, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
d = open(path, 'rb').read()
sanity, version, lo, ls, do, ds, so, ss = struct.unpack_from('<IiIIIIII', d, 0)
assert sanity == 0xFAB11BAF, hex(sanity)

lits = []
for i in range(ls // 8):
    length, idx = struct.unpack_from('<Ii', d, lo + i * 8)
    lits.append(d[do + idx: do + idx + length].decode('utf-8', 'replace'))
json.dump(lits, open(os.path.join(out, 'literals.json'), 'w', encoding='utf-8'), ensure_ascii=False)

names = d[so: so + ss].split(b'\x00')
idents = [n.decode('utf-8', 'replace') for n in names]
json.dump(idents, open(os.path.join(out, 'identifiers.json'), 'w', encoding='utf-8'), ensure_ascii=False)
print(f'version {version}: literals {len(lits)}, identifiers {len(idents)}')
