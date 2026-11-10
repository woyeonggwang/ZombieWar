# RL_FPSGame (구 ZombieWar) — CLAUDE.md

이전 계정과 현재 계정이 프로젝트 정보를 공유하기 위한 문서입니다. 확인된 사실만 적고, 확인되지 않은 것은 **확인 필요**로 표시합니다.

출처 표기
- [프로젝트]: 프로젝트 파일에서 직접 확인
- [기존 기록]: 이전 계정 대화 기록을 사용자가 정리하고 확인한 내용
- [사용자]: 사용자 진술

---

## 1. 개요

- 소규모 개인 프로젝트입니다. [사용자]
- 2025년부터 구상하던 미로 좀비 FPS 게임 프로젝트에서, AI 수업을 계기로 Unity ML-Agents 강화학습을 시도했습니다. 기존 Unity 활용 능력으로 강화학습을 시도한 사례로 포지셔닝합니다. [사용자]
- 현재 목표는 미로에서 엄폐하며 싸우는 3대3 팀전투 에이전트입니다. [사용자]
  - 미로와 무기 3종(Pistol, AssaultRifle, Minigun)은 원래 좀비 게임의 흔적입니다.
  - 미로는 엄폐 전투를 위해 유지했습니다.
  - 무기 해금 구조는 강화학습과 무관합니다.
- 이름 [프로젝트]
  - 제품 이름: RL_FPSGame (2026-10-20 변경, 이전 ZombiWar)
  - 아직 옛 이름인 것: 폴더 D:\Unity\ZombieWar, .sln, GitHub woyeonggwang/ZombieWar
  - 작업 브랜치: RL

## 2. 환경과 버전

**Unity 쪽** [프로젝트]
- Unity 6000.3.17f1
- URP 17.3.0
- ML-Agents (Unity 패키지) 4.0.3
- Input System 1.19.0
- AI Navigation 2.0.12
- Unity MCP (com.coplaydev.unity-mcp)
- 추론 백엔드 com.unity.ai.inference 2.6.1 [기존 기록]

**Python 쪽**
- Python 3.10.12, Anaconda conda 환경 mlagents (C:\Users\USER\anaconda3\envs\mlagents) [프로젝트: results/*/run_logs/timers.json]
- mlagents 1.1.0 [기존 기록]
- PyTorch [기존 기록]
  - 2.5.1+cu121은 RTX 5070(sm_120)에서 실행되지 않아 cu128 빌드로 교체했습니다.
  - 정확한 버전은 **확인 필요**입니다.

**하드웨어**
- 학습: RTX 5070 데스크탑 (호스트명 DESKTOP-0UN2TD0) [기존 기록, 호스트명은 프로젝트]
- 개발: GPU가 없는 노트북 [기존 기록]

## 3. 구조 [프로젝트]

**씬 (Assets/00.Scenes)**
- SampleScene: 학습용, 빌드에 등록됨
- PreBattleScene: 학습된 모델로 추론 전투

**스크립트 (Assets/01.Script)**
- 00.Map: MazeGenerator, MazeCell
- 01.Player: 플레이어, 총기, UI, 미니맵
- 02.Zombie
- 03.RL: PlayerAgent, TeamBattleManager, AgentHealth
- 04.Battle: BattleRunner, BattleUnit, HumanBattleUnit, 진단 도구
  - 진단 도구: BattleDiagnostics, ActionDiagnostics, ObsActionRecorder, ObsCsvReplay, OnnxVerifier, Editor/MLDebugMenu
- 99.System

**모델과 학습 파일**
- Assets/06.Model/MazeBattle_fixed.onnx: 현재 추론 모델입니다. MazeBattle02 최종 모델(20000187)을 재패킹한 것입니다.
- config/MazeBattle.yaml: 학습 설정 원본
- results/MazeBattle, results/MazeBattle01, results/MazeBattle02: 학습 결과, 체크포인트, tfevents

**루트 파일**
- Python: onnx_repack.py, onnx_crosscheck.py, onnx_layers.py
  - 경로가 D:\Unity\ZombieWar로 하드코딩되어 있습니다. 폴더 이름을 바꾸면 함께 수정해야 합니다.
- 로그
  - battle_diag.log, battle_probe.log (2026-07-24)
  - action_diag.log (2026-08-28)
  - obs_ONNX*.csv, *_summary.txt

## 4. 학습 설정 [프로젝트: config/MazeBattle.yaml, results/MazeBattle02/configuration.yaml]

**트레이너**
- trainer_type: poca (MA-POCA)
- self-play
  - save_steps 50000
  - team_change 200000
  - swap_steps 2000
  - window 10
  - play_against_latest_model_ratio 0.5
  - initial_elo 1200

**하이퍼파라미터**
- batch 2048, buffer 20480
- learning_rate 3e-4 (constant)
- beta 0.005, epsilon 0.2, lambd 0.95, num_epoch 3
- gamma 0.99
- 네트워크: hidden 256 × 2층, normalize false
- max_steps 20,000,000
- time_horizon 1000
- summary_freq 50000
- checkpoint_interval 500000

**실행 환경**
- time_scale 20
- DecisionPeriod 5 [프로젝트: 로그]
- fixedDeltaTime 0.02 [프로젝트: 로그]

## 5. 에이전트 설계

**관측: 89 = RayPerceptionSensor3D 65 + Vector 24** [프로젝트: obs 기록]
- 2026-07-23에 13 → 16 → 21 → 22 → 23 → 24 순서로 확장했습니다. [기존 기록]
- PlayerAgent.cs 요약 주석의 13개는 옛 값입니다.

**액션** [프로젝트]
- 연속 3개: 이동 X, 이동 Z, 회전
- 이산 1개: 발사

**액션 설계** [사용자]
- 회전을 이동과 분리, 방향별 속도 배율, 발사 중 감속은 사용자가 지시한 설계입니다.

**보상 셰이핑** [사용자]
- 여러 차례 테스트를 거쳐 사용자가 결정했습니다.
- 보상 값은 TeamBattleManager가 시작할 때 에이전트에 덮어씁니다. 따라서 값 조정은 매니저에서 합니다. [프로젝트]
- 기록에 남은 계기 [기존 기록]
  - centerDistancePenalty: −0.1이 누적되면 승리 보상을 압도해서 −0.0005로 낮췄습니다.
  - hitEnemyReward: 값이 4일 때 맞히기만 하는 전략이 최적이 되어 낮췄습니다.
  - drawPenalty: 소극적 무승부가 많아 사용자가 추가했습니다.
  - stall: 제자리 정지 문제
  - no-enemy: 적 탐색 실패 문제
  - hold-fire: 원인은 fireCostPenalty 때문에 사격이 손해였던 것입니다.
  - aimAlignReward: 상한을 두었습니다. 조준만으로 +40이 쌓였기 때문입니다.
  - spotEnemyReward: 에피소드당 1회로 제한했습니다.
  - 무기 선택 보상
  - 아군 사격: 비활성화했다가 감점과 함께 다시 활성화했습니다.
  - 총구 막힘

**학습 씬 구성** [기존 기록]
- 아레나 16개, 에이전트 96명

## 6. 학습 이력 [프로젝트: results/*]

모델 입력 차원은 체크포인트 .onnx.data 크기로 구분됩니다. 관측 13개는 349,184 B, 관측 24개(입력 89)는 360,448 B입니다. 두 크기의 차이는 11 × 256 × 4 B로, 정확히 관측 11개 차이와 같습니다.

**MazeBattle**
- 2026-07-23 00:4x ~ 01:15
- 1,688,891 스텝에서 종료
- 관측 13개 기준

**MazeBattle01**
- 2026-07-22 ~ 23
- 체크포인트 .onnx.data 크기가 349,184 → 352,256 → 357,376 → 360,448 B로 바뀝니다. 관측을 바꿀 때마다 --force로 다시 시작한 흔적입니다.
- 마지막 체크포인트는 6,139,848 스텝입니다.

**MazeBattle02 (최종)**
- 2026-07-24 14:4x ~ 21:26, 20,000,187 스텝
- 모든 체크포인트가 360,448 B입니다. 즉 처음부터 관측 24개(입력 89) 기준으로 학습했습니다.
- 소요 시간
  - 50만 스텝당 약 9분이 걸렸습니다.
  - 16:10 ~ 16:58 사이에 중단과 재개가 1회 있었습니다.
  - 시작부터 끝까지 약 6시간 40분, 중단 구간을 빼면 실학습 약 5시간 50분입니다. 사용자의 기억(약 3시간)과 다르므로 이 기록 값을 우선합니다.
- 체크포인트 reward (self-play 평균 보상)
  - 18.5M: 1.53
  - 19.0M: 1.28
  - 19.5M: −0.002
  - 20.0M: 1.10
- TensorBoard 지표 [프로젝트: tfevents, 2026-10-20 파싱]
  - 기록 구간: 14:49 ~ 16:09, 16:51 ~ 20:13, 20:19 ~ 21:26 (중단 2회, 실학습 약 5시간 50분)
  - Self-play/ELO: 1,200 → 4,059, 학습 내내 거의 직선으로 상승했습니다.
  - Environment/Cumulative Reward: 초반 최저 −7.99 → 약 1.5M 스텝에서 0 돌파 → 18~20M 구간 평균 약 1.12
  - Environment/Episode Length: 최대 1,471 → 2M 부근 약 160까지 감소 → 이후 약 270~290으로 다시 증가 (단위 해석은 확인 필요)
  - Policy/Entropy: 2.11 → 1.54, 꾸준히 감소했습니다.
  - Python 연결 추론(07-25 00시대): ELO 약 4,070~4,095, 보상 1.0~1.6
  - 그래프 원본 PNG는 Claude 대화에서 전달했습니다. 프로젝트 안에는 저장하지 않았습니다.
- 2026-07-25 00:11 ~ 00:31에는 --resume --inference로 Python 연결 추론을 실행했습니다 (configuration.yaml의 inference: true, tfevents 3개).

**운영 규칙** [사용자]
- 보상 제도를 바꿀 때마다 --force로 새로 학습합니다.
- 현재 MazeBattle02가 최종 학습 결과입니다.

## 7. 시행착오 (증상 → 원인 → 해결) [기존 기록]

**1. RayPerceptionSensor.DetectableTags가 비어 있음 (2026-07-23)**
- 사용자가 검증해서 발견했습니다.
- 에이전트가 적, 아군, 벽을 구분하지 못했습니다.

**2. InferenceDevice = ComputeShader에서 에디터 크래시**
- 원인: GPU 텐서를 CPU에서 인덱싱했습니다.
- 해결: Burst로 변경했습니다.

**3. 관측 크기 불일치로 인한 크래시**
- 증상: 크래시 위치가 Gun.Fire → Instantiate로 찍혀 총기 문제로 오인했습니다.
- 진단: BattlePreStepProbe로 DecideAction 직후에 죽는다는 것을 확인했습니다.
- 원인: 13개용 모델에 24개 관측을 넣어 네이티브 힙이 손상되었습니다.
- 해결: 새 run으로 다시 학습했습니다.

**4. manager = NULL (실행 순서 문제)**
- 해결: 실행 순서 −9000의 BattleEarlyWiring을 추가했습니다.

**5. BehaviorType = Default**
- 에이전트 96명 전원이 이 상태였습니다.
- 해결: InferenceOnly로 변경했습니다.

**6. ONNX 외부 데이터 오프셋 오류 (2026-07-30)**
- 증상
  - Python에 연결한 추론은 100점, Unity 단독 InferenceOnly는 10점이었습니다.
  - 이동 출력은 0.1~0.2, 회전만 최대치가 나와 제자리에서 돌았습니다.
  - timeScale을 20으로 맞춰도 같아서 timeScale 가설은 기각했습니다.
- 원인
  - .onnx(44KB)와 .onnx.data(360KB)로 나뉜 형식이었습니다.
  - Unity와 onnxruntime의 계층별 값을 비교한 결과, 은닉층은 일치했습니다.
  - 출력 헤드(256×3, 256×2)는 가중치 경계가 어긋나 있었습니다.
- 해결: onnx.save_model(..., save_as_external_data=False)로 단일 파일로 재패킹했습니다. onnxruntime과의 최대 오차는 0입니다.
- **규칙: 학습이 끝날 때마다 onnx_repack.py를 실행합니다.**
- 재현 확인 (2026-10-27) [프로젝트]
  - 원본 MazeBattle-20000187.onnx(+.onnx.data)를 Unity Inference Engine 2.6.1(CPU)에서 실행하면, 입력 (i%10)/10·(i%7)/7 기준 출력이 [-0.106, 0.103, -1.000]입니다. onnx_crosscheck.py에 기록된 7/30 측정값과 동일합니다.
  - MazeBattle_fixed.onnx는 [-1.000, 0.974, 0.793]으로, onnx_repack.py에 적힌 onnxruntime 기대값과 일치합니다.
  - 원본 모델을 Arena_0에 넣으면 에이전트가 짧게 표류하며 회전만 하는 증상이 지금도 화면으로 재현됩니다.

**7. 총알 방향 오류와 벽 관통**
- 원인: AddRelativeForce가 좌표계를 섞어 썼습니다. 또 일부 벽에 태그가 없었습니다.

**로그 보존 상태** [프로젝트]
- 루트의 battle_probe.log, battle_diag.log(2026-07-24 23:42)는 새 모델(관측 24개) 적용 후의 정상 종료 실행입니다. 크래시가 난 실행의 로그는 남아 있지 않습니다.

**8. FollowCamera 떨림**
- 해결했습니다. 세부 내용은 **확인 필요**입니다.

**사용자가 기각한 가설**
- 총알 폭증
- 팀 태그 혼동
- 미로 밀도 차이
- timeScale 차이
- --deterministic Python 추론은 테스트한 적이 없습니다.

## 8. 확정된 설계 결정 [사용자]

- 미로는 원래 구조를 단순화한 형태(벽 50% 제거)로 유지합니다. 십자형 구조는 되돌렸습니다.
- 아레나는 줄이지 않고 늘렸습니다: 1 → 9 → 16.
- 무승부 감점을 도입했습니다. 아군 사격은 감점과 함께 활성화합니다.
- 원본 스크립트를 수정하지 않고 래퍼로 확장한다는 제약을 세웠다가, 버그 수정을 위해 해제했습니다.

## 9. AI 협업 범위 [기존 기록]

**Claude가 한 일 (Unity MCP 사용)**
- 학습 스크립트 작성과 수정
- 96명 에이전트의 속성 일괄 변경
- 진단 도구와 onnx_repack.py 제작
- 가설 수립과 교차 검증

**사용자가 한 일**
- 게임 규칙, 보상 제도, 액션 설계 결정
- 구조 방향 결정
- 잘못된 가설 기각
- 학습 실행, 관찰, 운영

## 10. 미해결 과제와 확인 필요 항목

**BattleRunner.cs 주석 6건 — 확인 필요**
- 주석 [1]은 레이 센서 태그 순서가 팀 간에 비대칭이라는 내용입니다(makeTeamSymmetric, flipRedInstead로 Red 태그를 뒤집음). 이는 사용자가 기각한 팀 태그 혼동 가설과 같은 내용으로 보이는데 코드에는 남아 있습니다. 실제로 효과가 있었는지 확인이 필요합니다.
- 나머지 [2] ~ [6]은 대화 기록과 아직 대조하지 못했습니다.
  - [2] aliveRatio 관측이 0
  - [3] 에피소드 경계 없음
  - [4] 라운드 길이 200초 vs 120초
  - [5] 스폰 방식 불일치
  - [6] cohesionRadius 정규화 값 미동기화

**현재 프로젝트에 없는 스크립트 — 확인 필요**
- BattlePreStepProbe, BattleEarlyWiring이 지금 프로젝트에 없습니다.
- 삭제되었는지, 다른 방식으로 대체되었는지 확인이 필요합니다.

**발사 비율 차이의 해석 — 확인 필요**
- obs_ONNX_ts20_summary.txt (mode=PY, 2026-07-25): 발사 약 0.2% (5/3000)
- obs_ONNX_summary.txt (mode=ONNX, 파일 시각 2026-08-27 09:42): 발사 약 50%
- 재패킹 전에 측정한 값인지, 이후 다시 측정했는지 확인이 필요합니다.

**아직 없는 것**
- 승률, 오사율 수치 (측정은 나중에 진행 예정)
- PyTorch 정확한 버전

**8/28 이후 미구현 과제**
- 플레이 영상/GIF
- BC+GAIL 모방학습
- --initialize-from을 이용한 보상 비교 실험
- WebGL 빌드
- 1인칭 뷰

**보류**
- 폴더 이름과 GitHub 저장소 이름을 RL_FPSGame으로 바꿀지 여부
