#!/usr/bin/env python3
"""Synthesises AVAJunoFirst's sound effects and tunes into ../src/AVAJunoFirst/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAJunoFirst", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship's shot: a quick rising zip up the grid.
    write("Fire.wav", tone(0.1, 900, 2400, "square", release=0.6), 0.18)
    write("Kill.wav", mix(tone(0.35, 1200, 150, "noise", release=0.5), tone(0.25, 700, 200, "square", release=0.6, gain=0.5)), 0.32)
    # The astronaut picked off: a bright run up, the bonus starting.
    write("Astronaut.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["C6", "E6", "G6", "C7", "E7", "G7"]]), 0.34)
    write("EnemyShot.wav", tone(0.18, 1400, 500, "tri", release=0.6), 0.2)
    write("Spawn.wav", cat(tone(0.1, 300, 600, "square", release=0.5), tone(0.1, 400, 800, "square", release=0.5)), 0.2)
    # The warp: a sweep down and up, with a shimmer.
    write("Warp.wav", mix(cat(tone(0.25, 1600, 200, "saw", release=0.4), tone(0.25, 200, 1600, "saw", release=0.6)), tone(0.5, 3000, 3000, "noise", attack=0.05, release=0.7, gain=0.25)), 0.3)
    write("Death.wav", cat(mix(tone(0.8, 1000, 40, "noise", release=0.3), tone(0.7, 500, 30, "saw", release=0.3, gain=0.7)), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["G5", "E5", "C5", "G4"]]), 0.48)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["G5", "C6", "E6", "G6"]]), 0.32)
    write("WaveClear.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.4) for n in ["C6", "G5", "C6", "E6", "G6", "C7"]]), 0.38)
    # Time up: a falling alarm, twice.
    write("Fury.wav", cat(*[tone(0.3, 900, 300, "square", release=0.3) for _ in range(2)]), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["A5", "E6", "A5", "E6", "A6"]]), 0.38)

    # The engine's hum through the fight: a low pulse with a slow figure (whole bars at 120 bpm).
    bass = [(n, 0.5) for n in ["C2", "C3"] * 4 + ["A1", "A2"] * 4 + ["F1", "F2"] * 4 + ["G1", "G2"] * 4]
    lead = [("C4", 2), ("E4", 2), ("A3", 2), ("C4", 2), ("F3", 2), ("A3", 2), ("G3", 2), ("B3", 2)]
    write("Music.wav", tune(120, lead, bass, kind="tri"), 0.18)

    # The title: a soaring, driving theme in C (an original composition).
    lead = [("C5", 0.5), ("G5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("B5", 0.5), ("G5", 0.5), ("A5", 1), ("F5", 1), ("G5", 2),
            ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 0.5), ("D6", 1), ("B5", 0.5), ("G5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C6", 1), ("C6", 2)]
    bass = [("C3", 1), ("G2", 1), ("C3", 1), ("E3", 1), ("F2", 1), ("A2", 1), ("G2", 2),
            ("C3", 1), ("G2", 1), ("G2", 1), ("B2", 1), ("F2", 1), ("G2", 1), ("C3", 2)]
    write("TitleTune.wav", cat(tune(138, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("G5", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("G4", 1), ("E4", 1), ("D4", 1), ("B3", 1), ("C4", 4)], [("C2", 2), ("A1", 2), ("G1", 2), ("C2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
