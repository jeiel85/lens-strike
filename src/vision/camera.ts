// 후면 카메라 영상을 video 요소에 연결한다. 영상은 이 기기 안에서만 쓰고 저장·전송하지 않는다.

export class CameraError extends Error {
  constructor(
    message: string,
    readonly reason: 'insecure' | 'unsupported' | 'denied' | 'notfound' | 'busy' | 'unknown',
  ) {
    super(message);
  }
}

export async function startCamera(video: HTMLVideoElement): Promise<MediaStream> {
  if (!window.isSecureContext) {
    throw new CameraError('카메라는 HTTPS 주소에서만 쓸 수 있습니다.', 'insecure');
  }
  if (!navigator.mediaDevices?.getUserMedia) {
    throw new CameraError('이 브라우저는 카메라를 지원하지 않습니다. Chrome 또는 Safari로 열어 주세요.', 'unsupported');
  }
  let stream: MediaStream;
  try {
    stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
    });
  } catch (err) {
    const name = err instanceof DOMException ? err.name : '';
    if (name === 'NotAllowedError' || name === 'SecurityError') {
      throw new CameraError('카메라 권한이 거부되었습니다. 브라우저 주소창의 권한 설정에서 카메라를 허용해 주세요.', 'denied');
    }
    if (name === 'NotFoundError' || name === 'OverconstrainedError') {
      throw new CameraError('사용할 수 있는 카메라를 찾지 못했습니다.', 'notfound');
    }
    if (name === 'NotReadableError' || name === 'AbortError') {
      throw new CameraError('다른 앱이 카메라를 쓰고 있습니다. 카메라를 쓰는 앱을 닫고 다시 시도해 주세요.', 'busy');
    }
    throw new CameraError(`카메라를 켜지 못했습니다: ${err instanceof Error ? err.message : String(err)}`, 'unknown');
  }
  video.srcObject = stream;
  video.muted = true;
  video.playsInline = true;
  await video.play();
  if (video.videoWidth === 0) {
    await new Promise<void>((resolve) => video.addEventListener('loadedmetadata', () => resolve(), { once: true }));
  }
  return stream;
}

export function stopCamera(video: HTMLVideoElement): void {
  const stream = video.srcObject as MediaStream | null;
  stream?.getTracks().forEach((t) => t.stop());
  video.srcObject = null;
}
