// MediaPipe Pose Landmarker로 영상 속 사람의 관절을 찾는다.
// wasm은 npm 패키지에서 번들에 함께 넣고(버전 일치), 모델 파일은 Google 공식 저장소에서 받는다.

import { PoseLandmarker } from '@mediapipe/tasks-vision';
import wasmLoaderPath from '@mediapipe/tasks-vision/vision_wasm_internal.js?url';
import wasmBinaryPath from '@mediapipe/tasks-vision/vision_wasm_internal.wasm?url';
import type { Landmark } from '../game/hit';

const MODEL_URL =
  'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task';

/** 한 화면에서 찾을 최대 인원. 1대1이지만 주변 사람 때문에 상대를 놓치지 않도록 여유를 둔다. */
const MAX_POSES = 3;

export class PoseDetector {
  private lastTs = -1;

  private constructor(
    private readonly landmarker: PoseLandmarker,
    readonly delegate: 'GPU' | 'CPU',
  ) {}

  static async create(): Promise<PoseDetector> {
    // FilesetResolver(CDN 경로 탐색)를 거치지 않고 번들된 파일 경로를 직접 넘긴다
    const fileset = { wasmLoaderPath, wasmBinaryPath };
    const make = (delegate: 'GPU' | 'CPU') =>
      PoseLandmarker.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: MODEL_URL, delegate },
        runningMode: 'VIDEO',
        numPoses: MAX_POSES,
        minPoseDetectionConfidence: 0.5,
        minPosePresenceConfidence: 0.5,
        minTrackingConfidence: 0.5,
      });
    try {
      return new PoseDetector(await make('GPU'), 'GPU');
    } catch (gpuErr) {
      console.warn('GPU 가속을 쓸 수 없어 CPU로 전환합니다.', gpuErr);
      return new PoseDetector(await make('CPU'), 'CPU');
    }
  }

  /** 현재 프레임의 사람별 관절. 같은 타임스탬프로 두 번 부르면 빈 결과를 돌려준다. */
  detect(video: HTMLVideoElement, nowMs: number): Landmark[][] {
    if (video.readyState < 2 || video.videoWidth === 0) return [];
    const ts = Math.max(nowMs, this.lastTs + 1);
    this.lastTs = ts;
    return this.landmarker.detectForVideo(video, ts).landmarks;
  }

  close(): void {
    this.landmarker.close();
  }
}
