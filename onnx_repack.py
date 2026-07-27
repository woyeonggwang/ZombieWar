import onnx, os
import numpy as np
import onnxruntime as ort

SRC = r'D:\Unity\ZombieWar\Assets\06.Model\MazeBattle02\MazeBattle\MazeBattle-20000187.onnx'
DST = r'D:\Unity\ZombieWar\Assets\06.Model\MazeBattle_fixed.onnx'

m = onnx.load(SRC)                      # .onnx.data 를 프로토 안으로 읽어들임
onnx.save_model(m, DST, save_as_external_data=False)
print('저장 완료 :', DST)
print('크기      :', os.path.getsize(DST), 'bytes  (원본 .onnx =', os.path.getsize(SRC), '+ .data =', os.path.getsize(SRC + '.data'), ')')
print('외부데이터 파일 동반 여부 :', os.path.exists(DST + '.data'))
print()

s = ort.InferenceSession(DST, providers=['CPUExecutionProvider'])
names = [o.name for o in s.get_outputs()]
o0 = np.array([[(i%10)/10.0 for i in range(65)]], np.float32)
o1 = np.array([[(i%7)/7.0 for i in range(24)]], np.float32)
feed = {'obs_0':o0, 'obs_1':o1, 'action_masks':np.ones((1,2),np.float32), 'recurrent_in':np.zeros((1,1,0),np.float32)}
r = dict(zip(names, s.run(None, feed)))
print('새 파일 onnxruntime deterministic_continuous_actions =', np.round(np.asarray(r['deterministic_continuous_actions']).ravel(), 6))
print('기대값 (원본과 동일해야 함)                          = [-1.        0.973691  0.793452]')
