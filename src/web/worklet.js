// Mikrofon sesini 16 bit mono parçalara (yaklaşık 20 ms) çevirir ve ana sayfaya yollar.
class Tap extends AudioWorkletProcessor {
  constructor() {
    super();
    this.size = Math.round(sampleRate / 50);
    this.buf = new Int16Array(this.size);
    this.n = 0;
    this.peak = 0;
  }
  process(inputs) {
    const input = inputs[0];
    const ch = input && input[0];
    if (ch) {
      for (let i = 0; i < ch.length; i++) {
        let s = ch[i];
        if (s > 1) s = 1; else if (s < -1) s = -1;
        const a = s < 0 ? -s : s;
        if (a > this.peak) this.peak = a;
        this.buf[this.n++] = s < 0 ? s * 32768 : s * 32767;
        if (this.n === this.size) {
          this.port.postMessage({ pcm: this.buf.buffer, peak: this.peak }, [this.buf.buffer]);
          this.buf = new Int16Array(this.size);
          this.n = 0;
          this.peak = 0;
        }
      }
    }
    return true;
  }
}
registerProcessor('tap', Tap);
