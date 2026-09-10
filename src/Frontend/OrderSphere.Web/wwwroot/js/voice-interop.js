window.advisorVoice = {
    _audioContext: null,
    _mediaStream: null,
    _scriptProcessor: null,
    _pcmChunks: [],
    _isRecording: false,

    async startRecording() {
        this._pcmChunks = [];
        this._isRecording = true;
        this._mediaStream = await navigator.mediaDevices.getUserMedia({
            audio: { sampleRate: 16000, channelCount: 1, echoCancellation: true, noiseSuppression: true }
        });
        this._audioContext = new AudioContext({ sampleRate: 16000 });
        const source = this._audioContext.createMediaStreamSource(this._mediaStream);
        this._scriptProcessor = this._audioContext.createScriptProcessor(4096, 1, 1);
        this._scriptProcessor.onaudioprocess = (e) => {
            if (this._isRecording) {
                this._pcmChunks.push(new Float32Array(e.inputBuffer.getChannelData(0)));
            }
        };
        source.connect(this._scriptProcessor);
        this._scriptProcessor.connect(this._audioContext.destination);
    },

    async stopRecording() {
        this._isRecording = false;
        if (this._scriptProcessor) { this._scriptProcessor.disconnect(); }
        if (this._audioContext) { await this._audioContext.close(); }
        if (this._mediaStream) { this._mediaStream.getTracks().forEach(t => t.stop()); }

        const totalLength = this._pcmChunks.reduce((sum, c) => sum + c.length, 0);
        const merged = new Float32Array(totalLength);
        let offset = 0;
        for (const chunk of this._pcmChunks) {
            merged.set(chunk, offset);
            offset += chunk.length;
        }

        const int16 = new Int16Array(merged.length);
        for (let i = 0; i < merged.length; i++) {
            int16[i] = Math.max(-32768, Math.min(32767, Math.round(merged[i] * 32768)));
        }

        return Array.from(new Uint8Array(encodeWav(int16, 16000)));
    },

    playAudio(base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        const blob = new Blob([bytes], { type: 'audio/mpeg' });
        const url = URL.createObjectURL(blob);
        const audio = new Audio(url);
        audio.onended = () => URL.revokeObjectURL(url);
        audio.play().catch(() => {});
    }
};

function encodeWav(samples, sampleRate) {
    const dataLength = samples.length * 2;
    const buffer = new ArrayBuffer(44 + dataLength);
    const view = new DataView(buffer);
    const setStr = (off, str) => {
        for (let i = 0; i < str.length; i++) view.setUint8(off + i, str.charCodeAt(i));
    };
    setStr(0, 'RIFF');
    view.setUint32(4, 36 + dataLength, true);
    setStr(8, 'WAVE');
    setStr(12, 'fmt ');
    view.setUint32(16, 16, true);       // PCM subchunk size
    view.setUint16(20, 1, true);        // PCM format
    view.setUint16(22, 1, true);        // mono
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * 2, true); // byte rate
    view.setUint16(32, 2, true);        // block align
    view.setUint16(34, 16, true);       // bits per sample
    setStr(36, 'data');
    view.setUint32(40, dataLength, true);
    for (let i = 0; i < samples.length; i++) {
        view.setInt16(44 + i * 2, samples[i], true);
    }
    return buffer;
}
