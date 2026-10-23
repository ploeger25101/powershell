#!/usr/bin/env python3
"""Procedurally composed dark chiptune music and retro sound effects for Project Abyss (original material).

Writes WAV files for sound effects and OGG (via oggenc, if installed) for looping music into
src/ProjectAbyss.Smapi/assets/audio/{sfx,music}. File names are the cue ids used by the game (AudioDirector).
Deterministic: the same script always produces the same audio.
"""
import os
import shutil
import subprocess
import wave
import numpy as np

SR = 22050
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "src", "ProjectAbyss.Smapi", "assets", "audio")
rng = np.random.default_rng(1337)


def note_freq(n):
    """MIDI note number -> Hz."""
    return 440.0 * 2 ** ((n - 69) / 12)


def t_axis(dur):
    return np.arange(int(SR * dur)) / SR


def pulse(freq, dur, duty=0.5):
    t = t_axis(dur)
    phase = (t * freq) % 1.0
    return np.where(phase < duty, 1.0, -1.0)


def triangle(freq, dur):
    t = t_axis(dur)
    phase = (t * freq) % 1.0
    return 4 * np.abs(phase - 0.5) - 1


def noise(dur, steps=1):
    n = int(SR * dur)
    base = rng.uniform(-1, 1, n // steps + 1)
    return np.repeat(base, steps)[:n]


def env(n, a=0.005, d=0.05, s=0.6, r=0.05, dur=None):
    total = n / SR
    a_n, d_n, r_n = int(a * SR), int(d * SR), int(r * SR)
    s_n = max(0, n - a_n - d_n - r_n)
    e = np.concatenate([np.linspace(0, 1, a_n, endpoint=False), np.linspace(1, s, d_n, endpoint=False), np.full(s_n, s), np.linspace(s, 0, r_n)])
    return np.pad(e, (0, max(0, n - len(e))))[:n]


def sweep(f0, f1, dur, kind="pulse", duty=0.5):
    t = t_axis(dur)
    f = np.linspace(f0, f1, len(t))
    phase = np.cumsum(f) / SR % 1.0
    if kind == "tri":
        return 4 * np.abs(phase - 0.5) - 1
    return np.where(phase < duty, 1.0, -1.0)


def write_wav(path, data):
    data = np.clip(data, -1, 1)
    pcm = (data * 32767 * 0.9).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


# ------------------------------------------------------------------ sound effects

def sfx():
    s = {}
    def shaped(x, **kw):
        return x * env(len(x), **kw)
    s["abyss_sfx_warrior_sword"] = shaped(mix(noise(0.12, 2) * 0.5, sweep(1400, 500, 0.12, duty=0.25) * 0.3), a=0.002, d=0.04, s=0.3, r=0.06)
    s["abyss_sfx_warrior_axe"] = shaped(mix(noise(0.16, 4) * 0.6, sweep(500, 160, 0.16) * 0.35), a=0.002, d=0.06, s=0.3, r=0.08)
    s["abyss_sfx_warrior_mace"] = shaped(mix(noise(0.2, 8) * 0.6, sweep(260, 60, 0.2, kind="tri") * 0.7), a=0.002, d=0.08, s=0.3, r=0.1)
    s["abyss_sfx_ability"] = shaped(mix(sweep(300, 900, 0.3, duty=0.125) * 0.4, sweep(150, 450, 0.3, kind="tri") * 0.4), a=0.01, d=0.1, s=0.5, r=0.15)
    s["abyss_sfx_dash"] = shaped(noise(0.18, 1) * np.linspace(0.2, 0.7, int(SR * 0.18)), a=0.01, d=0.05, s=0.6, r=0.1)
    s["abyss_sfx_hit"] = shaped(mix(noise(0.07, 3) * 0.6, pulse(180, 0.07) * 0.3), a=0.001, d=0.03, s=0.2, r=0.03)
    s["abyss_sfx_crit"] = shaped(mix(noise(0.1, 2) * 0.5, sweep(1200, 1800, 0.1, duty=0.25) * 0.35), a=0.001, d=0.04, s=0.3, r=0.05)
    s["abyss_sfx_player_hit"] = shaped(mix(noise(0.15, 6) * 0.6, sweep(220, 90, 0.15) * 0.5), a=0.001, d=0.05, s=0.4, r=0.08)
    s["abyss_sfx_parry"] = shaped(mix(pulse(1568, 0.25, 0.125) * 0.35, pulse(2093, 0.25, 0.25) * 0.25), a=0.001, d=0.08, s=0.3, r=0.15)
    s["abyss_sfx_enemy_die"] = shaped(mix(sweep(400, 80, 0.3) * 0.4, noise(0.3, 10) * 0.4), a=0.002, d=0.1, s=0.4, r=0.15)
    s["abyss_sfx_boss_die"] = shaped(mix(sweep(300, 40, 1.4, kind="tri") * 0.7, noise(1.4, 16) * 0.5), a=0.01, d=0.4, s=0.5, r=0.8)
    s["abyss_sfx_telegraph"] = shaped(pulse(880, 0.12, 0.25) * 0.3, a=0.002, d=0.03, s=0.6, r=0.04)
    # boss warning: two low urgent pulses, loud and distinct
    w1 = shaped(pulse(110, 0.18, 0.5) * 0.6 + pulse(220, 0.18, 0.25) * 0.3, a=0.002, d=0.05, s=0.8, r=0.05)
    s["abyss_sfx_boss_warning"] = np.concatenate([w1, np.zeros(int(SR * 0.06)), w1])
    s["abyss_sfx_boss_phase"] = shaped(mix(sweep(55, 110, 1.0) * 0.5, noise(1.0, 20) * 0.3, sweep(110, 220, 1.0, kind="tri") * 0.4), a=0.05, d=0.3, s=0.6, r=0.4)
    s["abyss_sfx_stagger"] = shaped(mix(pulse(330, 0.4, 0.125) * 0.3, pulse(247, 0.4, 0.125) * 0.3, noise(0.4, 6) * 0.2), a=0.002, d=0.1, s=0.4, r=0.2)
    s["abyss_sfx_potion"] = shaped(sweep(400, 1200, 0.35, kind="tri") * 0.6, a=0.01, d=0.1, s=0.6, r=0.15)
    s["abyss_sfx_downed"] = shaped(mix(sweep(440, 110, 0.8, duty=0.25) * 0.5), a=0.01, d=0.2, s=0.5, r=0.3)
    s["abyss_sfx_revive"] = np.concatenate([shaped(pulse(note_freq(n), 0.09, 0.25) * 0.4, a=0.002, d=0.02, s=0.6, r=0.03) for n in (60, 64, 67, 72)])
    s["abyss_sfx_clear"] = np.concatenate([shaped(pulse(note_freq(n), 0.14, 0.5) * 0.35 + triangle(note_freq(n - 12), 0.14) * 0.3, a=0.002, d=0.04, s=0.6, r=0.05) for n in (57, 60, 64, 69, 72)])
    s["abyss_sfx_ui_click"] = shaped(pulse(1046, 0.04, 0.25) * 0.3, a=0.001, d=0.01, s=0.4, r=0.02)
    s["abyss_sfx_loot"] = np.concatenate([shaped(pulse(note_freq(n), 0.06, 0.125) * 0.3, a=0.001, d=0.02, s=0.5, r=0.02) for n in (76, 79, 84)])
    return s


# ------------------------------------------------------------------ music

class Song:
    def __init__(self, bpm, bars, beats=4):
        self.bpm = bpm
        self.step = 60.0 / bpm / 4  # sixteenth notes
        self.length = int(SR * self.step * 16 * bars)
        self.buf = np.zeros(self.length)

    def place(self, sig, step_index, gain):
        start = int(step_index * self.step * SR) % self.length
        end = start + len(sig)
        if end <= self.length:
            self.buf[start:end] += sig * gain
        else:  # wrap around so the loop is seamless
            k = self.length - start
            self.buf[start:] += sig[:k] * gain
            self.buf[: end - self.length] += sig[k:] * gain

    def note(self, kind, midi, step_index, steps, gain, duty=0.5):
        dur = self.step * steps
        f = note_freq(midi)
        sig = pulse(f, dur, duty) if kind == "pulse" else triangle(f, dur)
        sig = sig * env(len(sig), a=0.003, d=0.05, s=0.55, r=min(0.08, dur / 3))
        self.place(sig, step_index, gain)

    def kick(self, i, gain=0.7):
        sig = sweep(140, 40, 0.12, kind="tri") * env(int(SR * 0.12), a=0.001, d=0.05, s=0.3, r=0.06)
        self.place(sig, i, gain)

    def snare(self, i, gain=0.35):
        sig = noise(0.1, 2) * env(int(SR * 0.1), a=0.001, d=0.03, s=0.25, r=0.05)
        self.place(sig, i, gain)

    def hat(self, i, gain=0.12):
        sig = noise(0.03, 1) * env(int(SR * 0.03), a=0.001, d=0.01, s=0.2, r=0.01)
        self.place(sig, i, gain)

    def render(self):
        b = self.buf
        peak = np.max(np.abs(b)) or 1
        return b / peak * 0.85


# Dark minor progressions (MIDI root notes) per mood
MINOR = [0, 2, 3, 5, 7, 8, 10]


def scale_note(root, degree):
    octave, idx = divmod(degree, len(MINOR))
    return root + 12 * octave + MINOR[idx]


def compose(bpm, root, prog, intensity, bars=8, arp=True, lead=None, seed=0):
    r = np.random.default_rng(seed)
    song = Song(bpm, bars)
    for bar in range(bars):
        chord = prog[bar % len(prog)]
        base = bar * 16
        # bass: root on the beat, octave bounce with higher intensity
        for s in range(0, 16, 4 if intensity < 2 else 2):
            song.note("tri", root - 24 + chord + (12 if intensity >= 2 and s % 4 == 2 else 0), base + s, 2, 0.5)
        # arpeggio over the chord (root, third, fifth, octave) in sixteenths
        if arp:
            tones = [chord, chord + 3, chord + 7, chord + 12]
            for s in range(16):
                if intensity == 0 and s % 2:
                    continue
                song.note("pulse", root + tones[s % 4] + (12 if intensity >= 2 and s >= 8 else 0), base + s, 1, 0.16, duty=0.125)
        # lead melody from the minor scale
        if lead is not None:
            for s in range(0, 16, 2):
                if r.random() < lead:
                    deg = int(r.integers(0, 9))
                    song.note("pulse", scale_note(root + 12, deg), base + s, 2 if r.random() < 0.6 else 4, 0.2, duty=0.25)
        # drums
        if intensity >= 1:
            for s in range(0, 16, 4):
                song.kick(base + s)
            song.snare(base + 4)
            song.snare(base + 12)
            for s in range(0, 16, 2):
                song.hat(base + s)
        if intensity >= 2:
            song.kick(base + 10, 0.5)
            for s in range(1, 16, 2):
                song.hat(base + s, 0.08)
        if intensity >= 3:
            song.snare(base + 14, 0.25)
            song.snare(base + 15, 0.3)
    return song.render()


def music():
    m = {}
    m["abyss_music_menu"] = compose(76, 45, [0, -4, -2, -5], 0, bars=8, lead=0.3, seed=1)
    m["abyss_music_merchant"] = compose(92, 50, [0, 5, 3, 7], 0, bars=8, lead=0.5, seed=2)
    m["abyss_music_biome1_calm"] = compose(80, 43, [0, 3, -2, 5], 0, bars=8, lead=0.2, seed=3)
    m["abyss_music_biome1_combat"] = compose(132, 43, [0, 0, 3, -2], 1, bars=8, lead=0.35, seed=4)
    m["abyss_music_biome1_elite"] = compose(140, 43, [0, 1, 0, -2], 2, bars=8, lead=0.45, seed=5)
    m["abyss_music_boss_forest_p1"] = compose(128, 40, [0, -4, 0, 3], 1, bars=8, lead=0.4, seed=6)
    m["abyss_music_boss_forest_p2"] = compose(140, 40, [0, 1, -4, -2], 2, bars=8, lead=0.55, seed=7)
    m["abyss_music_boss_forest_p3"] = compose(156, 40, [0, 1, 0, 6], 3, bars=8, lead=0.7, seed=8)
    for p in ("p1", "p2", "p3"):
        m[f"abyss_music_boss_forest_{p}_pact"] = compose(160, 39, [0, 6, 1, 0], 3, bars=8, lead=0.75, seed=9)
    m["abyss_music_pact"] = compose(60, 38, [0, 1, 0, -1], 0, bars=4, arp=False, lead=0.25, seed=10)
    m["abyss_music_victory"] = compose(110, 48, [0, 5, 7, 12], 1, bars=4, lead=0.6, seed=11)
    m["abyss_music_defeat"] = compose(56, 41, [0, -1, -2, -5], 0, bars=4, lead=0.15, seed=12)
    return m


def main():
    os.makedirs(os.path.join(OUT, "sfx"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "music"), exist_ok=True)
    for name, data in sfx().items():
        write_wav(os.path.join(OUT, "sfx", name + ".wav"), data)
    have_oggenc = shutil.which("oggenc") is not None
    for name, data in music().items():
        wav = os.path.join(OUT, "music", name + ".wav")
        write_wav(wav, data)
        if have_oggenc:
            subprocess.run(["oggenc", "-Q", "-q", "3", "-o", wav[:-4] + ".ogg", wav], check=True)
            os.remove(wav)
    print("audio written to", OUT, "(music as", "ogg" if have_oggenc else "wav", ")")


if __name__ == "__main__":
    main()
