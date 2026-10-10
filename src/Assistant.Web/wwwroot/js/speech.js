const Recognition = window.SpeechRecognition ?? window.webkitSpeechRecognition;
const SILENCE_MS = 2000;
const NO_SPEECH_MS = 8000;

export const SPEECH_ERRORS = {
  'not-allowed': 'denied',
  'service-not-allowed': 'denied',
  'audio-capture': 'no-mic',
  'no-speech': 'no-speech',
  network: 'network',
};

export class SpeechInput {
  #recognition = null;
  #finalText = '';
  #cancelled = false;
  #level = null;
  #silenceTimer = 0;

  constructor({ onInterim, onFinal, onEnd, onError, onLevel }) {
    Object.assign(this, { onInterim, onFinal, onEnd, onError, onLevel });
  }

  static get isSupported() {
    return Boolean(Recognition);
  }

  get isListening() {
    return this.#recognition !== null;
  }

  start() {
    if (!Recognition || this.isListening) return false;

    const recognition = new Recognition();
    recognition.lang = 'ru-RU';
    recognition.interimResults = true;
    recognition.continuous = true;
    recognition.maxAlternatives = 1;

    this.#finalText = '';
    this.#cancelled = false;
    let heard = '';

    recognition.onresult = (event) => {
      let final = '';
      let interim = '';
      for (const result of event.results) {
        if (result.isFinal) final += result[0].transcript;
        else interim += result[0].transcript;
      }
      this.#finalText = final.trim();
      heard = `${final} ${interim}`.trim();
      this.onInterim?.(this.#finalText, interim.trim());
      this.#armSilence(heard ? SILENCE_MS : NO_SPEECH_MS);
    };
    recognition.onerror = (event) => {
      if (event.error === 'aborted') return;
      this.#cancelled = true;
      this.onError?.(SPEECH_ERRORS[event.error] ?? 'unknown');
    };
    recognition.onend = () => {
      clearTimeout(this.#silenceTimer);
      this.#recognition = null;
      this.#stopLevel();
      const text = (this.#finalText || heard).trim();
      if (this.#cancelled) this.onEnd?.({ cancelled: true });
      else if (text) this.onFinal?.(text);
      else {
        this.onError?.('no-speech');
        this.onEnd?.({ cancelled: true });
      }
    };

    this.#recognition = recognition;
    try {
      recognition.start();
    } catch {
      this.#recognition = null;
      this.onError?.('unknown');
      return false;
    }
    this.#startLevel();
    this.#armSilence(NO_SPEECH_MS);
    return true;
  }

  #armSilence(delay) {
    clearTimeout(this.#silenceTimer);
    this.#silenceTimer = setTimeout(() => this.#recognition?.stop(), delay);
  }

  stop() {
    this.#recognition?.stop();
  }

  cancel() {
    if (!this.#recognition) return;
    this.#cancelled = true;
    this.#recognition.abort();
  }

  async #startLevel() {
    if (!navigator.mediaDevices?.getUserMedia || !window.AudioContext) return;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!this.#recognition) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }
      const context = new AudioContext();
      const analyser = context.createAnalyser();
      analyser.fftSize = 256;
      context.createMediaStreamSource(stream).connect(analyser);
      const data = new Uint8Array(analyser.frequencyBinCount);
      const tick = () => {
        if (!this.#level) return;
        analyser.getByteFrequencyData(data);
        const average = data.reduce((sum, value) => sum + value, 0) / data.length;
        this.onLevel?.(Math.min(1, average / 90));
        this.#level.frame = requestAnimationFrame(tick);
      };
      this.#level = { stream, context, frame: 0 };
      tick();
    } catch {
      this.#level = null;
    }
  }

  #stopLevel() {
    if (!this.#level) return;
    cancelAnimationFrame(this.#level.frame);
    this.#level.stream.getTracks().forEach((track) => track.stop());
    this.#level.context.close().catch(() => {});
    this.#level = null;
    this.onLevel?.(0);
  }
}

const CHARS_PER_SECOND = 14;

const START_GRACE_MS = 1500;
const WATCH_INTERVAL_MS = 300;

export class Speaker {
  #listeners = new Set();
  #current = null;
  #watch = 0;
  #unlocked = false;

  get isSupported() {
    return 'speechSynthesis' in window;
  }

  get isSpeaking() {
    return this.#current !== null;
  }

  get currentKey() {
    return this.#current;
  }

  estimateSeconds(text) {
    return text.length / CHARS_PER_SECOND;
  }

  onChange(listener) {
    this.#listeners.add(listener);
  }

  unlock() {
    if (!this.isSupported || this.#unlocked) return;
    this.#unlocked = true;
    const silent = new SpeechSynthesisUtterance(' ');
    silent.volume = 0;
    speechSynthesis.speak(silent);
  }

  speak(text, key = text) {
    if (!this.isSupported || !text) return;
    speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'ru-RU';
    const voice = speechSynthesis.getVoices().find((candidate) => candidate.lang?.toLowerCase().startsWith('ru'));
    if (voice) utterance.voice = voice;
    utterance.onend = utterance.onerror = () => {
      if (this.#current === key) this.#set(null);
    };
    this.#set(key);
    speechSynthesis.speak(utterance);
    this.#watchUntilSilent(key, text);
  }

  stop() {
    if (!this.isSupported) return;
    speechSynthesis.cancel();
    this.#set(null);
  }

  #watchUntilSilent(key, text) {
    clearInterval(this.#watch);
    const startedAt = Date.now();
    const deadline = startedAt + START_GRACE_MS + this.estimateSeconds(text) * 2000;
    this.#watch = setInterval(() => {
      if (this.#current !== key) {
        clearInterval(this.#watch);
        return;
      }
      const idle = !speechSynthesis.speaking && !speechSynthesis.pending;
      if ((idle && Date.now() - startedAt > START_GRACE_MS) || Date.now() > deadline) {
        clearInterval(this.#watch);
        if (Date.now() > deadline) speechSynthesis.cancel();
        this.#set(null);
      }
    }, WATCH_INTERVAL_MS);
  }

  #set(key) {
    this.#current = key;
    this.#listeners.forEach((listener) => listener(key));
  }
}
