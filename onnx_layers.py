import numpy as np, onnx, onnxruntime as ort
from onnx import helper
MODEL = r'D:\Unity\ZombieWar\Assets\06.Model\MazeBattle02\MazeBattle\MazeBattle-20000187.onnx'
m = onnx.load(MODEL)
outs = set(o.name for o in m.graph.output)
for node in m.graph.node:
    for o in node.output:
        if o not in outs:
            m.graph.output.append(helper.make_empty_tensor_value_info(o))
            outs.add(o)
s = ort.InferenceSession(m.SerializeToString(), providers=['CPUExecutionProvider'])
o0 = np.array([[(i%10)/10.0 for i in range(65)]], np.float32)
o1 = np.array([[(i%7)/7.0 for i in range(24)]], np.float32)
feed = {'obs_0':o0, 'obs_1':o1, 'action_masks':np.ones((1,2),np.float32), 'recurrent_in':np.zeros((1,1,0),np.float32)}
names = [o.name for o in s.get_outputs()]
res = dict(zip(names, s.run(None, feed)))
print('%-3s %-18s %-14s %-10s %s' % ('#','op','shape','absMean','first4'))
for i, node in enumerate(m.graph.node):
    for o in node.output:
        v = res.get(o)
        if v is None: continue
        a = np.asarray(v).astype(np.float64).ravel()
        am = float(np.abs(a).mean()) if a.size else 0.0
        print('%-3d %-18s %-14s %-10.6f %s' % (i, node.op_type, str(np.asarray(v).shape), am, np.round(a[:4],6)))
