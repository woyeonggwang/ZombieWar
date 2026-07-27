import os, sys, glob
import numpy as np

MODEL = r'D:\Unity\ZombieWar\Assets\06.Model\MazeBattle02\MazeBattle\MazeBattle-20000187.onnx'

# Unity Inference Engine 이 같은 입력에 대해 낸 값 (측정 완료)
UNITY = {
 0: [-0.014648, 0.003367, -0.097031],
 1: [-0.106088, 0.103473, -1.000000],
 2: [-0.045920, 0.036416, -1.000000],
}
UNITY_W = dict(absMean=0.095334, wmin=-0.5141, wmax=0.5506)

def cases():
    z0 = np.zeros((1,65), np.float32); z1 = np.zeros((1,24), np.float32)
    p0 = np.array([[(i % 10)/10.0 for i in range(65)]], np.float32)
    p1 = np.array([[(i % 7)/7.0 for i in range(24)]], np.float32)
    o0 = np.ones((1,65), np.float32); o1 = np.ones((1,24), np.float32)
    return [(z0,z1),(p0,p1),(o0,o1)]

print('=== 1. onnxruntime vs Unity Inference Engine (같은 입력, 같은 .onnx) ===')
try:
    import onnxruntime as ort
    s = ort.InferenceSession(MODEL, providers=['CPUExecutionProvider'])
    names = [o.name for o in s.get_outputs()]
    innames = [i.name for i in s.get_inputs()]
    print('inputs :', innames)
    for k,(a,b) in enumerate(cases()):
        feed = {}
        if 'obs_0' in innames: feed['obs_0'] = a
        if 'obs_1' in innames: feed['obs_1'] = b
        if 'action_masks' in innames: feed['action_masks'] = np.ones((1,2), np.float32)
        if 'recurrent_in' in innames: feed['recurrent_in'] = np.zeros((1,1,0), np.float32)
        out = s.run(None, feed)
        d = dict(zip(names, out))
        det = np.array(d['deterministic_continuous_actions']).ravel()
        u = np.array(UNITY[k])
        print('CASE', k, ' onnxruntime =', np.round(det,6), ' Unity =', u, ' maxdiff =', round(float(np.abs(det-u).max()),6))
except ImportError:
    print('onnxruntime 미설치 ->  pip install onnxruntime')
except Exception as e:
    print('실패:', type(e).__name__, e)

print()
print('=== 2. checkpoint.pt 가중치 vs ONNX 가중치 ===')
try:
    import torch
    cands = glob.glob(r'D:\Unity\ZombieWar\results\**\checkpoint.pt', recursive=True)
    cands += glob.glob(r'results\**\checkpoint.pt', recursive=True)
    if len(sys.argv) > 1: cands = [sys.argv[1]]
    if not cands:
        print('checkpoint.pt 를 못 찾았습니다. 인자로 경로를 넘기세요:  python onnx_crosscheck.py <경로>')
    else:
        cands.sort(key=os.path.getmtime)
        ck_path = cands[-1]
        print('checkpoint:', ck_path)
        ck = torch.load(ck_path, map_location='cpu', weights_only=False)
        sd = ck.get('Policy', ck) if isinstance(ck, dict) else ck
        found = False
        for k, v in sd.items():
            if hasattr(v, 'shape') and tuple(v.shape) in [(256,89),(89,256)]:
                w = v.detach().float()
                print('  .pt   ', k, tuple(v.shape), 'absMean=%.6f min=%.4f max=%.4f' % (w.abs().mean().item(), w.min().item(), w.max().item()))
                print('  .onnx ', '(89, 256)          absMean=%.6f min=%.4f max=%.4f' % (UNITY_W['absMean'], UNITY_W['wmin'], UNITY_W['wmax']))
                found = True
        if not found:
            print('(89,256) 텐서를 못 찾았습니다. 키 목록:')
            for k, v in list(sd.items())[:40]:
                print('   ', k, getattr(v, 'shape', ''))
except ImportError:
    print('torch 미설치')
except Exception as e:
    print('실패:', type(e).__name__, e)
