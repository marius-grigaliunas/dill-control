# 0007 A second autoload: `AudioDirector`

*8 October 2026, ported from the lab*

## Decision

`AudioDirector` is an autoload beside `EventBus`. It owns a pool of audio
players and is the only code that creates one. Callers name an `Sfx` id or hand
it a `SoundEvent`.

## Why an autoload

A sound has to outlive whatever asked for it. A rifle fired on the frame it is
holstered, an explosion effect that frees itself at once, a pickle that dies
and is recycled the same frame: in all of them the emitter is gone before the
sound's tail is. A pool parented to something that persists is the only
arrangement that does not cut those off, and nothing below the scene root
persists.

It also caps voices. Hundreds of pickles can each want a death sound in the
same second, and the mix breaks before the frame does.

## Placeholders

Every sound starts as a recipe in `SoundLibrary`, synthesised on first use and
prewarmed a couple a frame. An empty `snd_bank_default.tres` means every slot
is a placeholder. A recording replaces one by being dropped into that slot's
`SoundEvent.Streams`; nothing else changes.
