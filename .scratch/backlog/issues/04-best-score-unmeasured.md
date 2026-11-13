# `ClimberState.BestScore` conflates "unmeasured" and "measured zero"

Status: open
Type: task

`ClimberState.BestScore` is a bare `int`, so `0` means both "no measurement yet"
and "a measurement of zero passing tests". The loop's own logic was adjusted to
live with this (see `.scratch/first-iteration-best/spec.md`), but the state model
still cannot express the difference to the UI or to future consumers.

Decide whether to make it `int?` (or carry a separate "measured" flag) and how the
telemetry cell renders the unmeasured case.
