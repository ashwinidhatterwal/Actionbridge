ActionBridge v0.6.0 GitHub build fix

Both Windows and Ubuntu jobs in run 37256028048 failed the same lost-final-acknowledgement test. Android passed.

The old test sampled the action counter after losing the finish reply, while the first receiver action might still be pending. Its normal completion during retry was then mistaken for duplicate execution.

The replacement uses an explicitly held test action and checks: the sender stays queued while that action is pending; retry completes exactly one action; the receiver has one byte-identical file. This changes tests only. Existing application binaries and backend do not need updating.

INSTALL THE FIX
1. Extract this ZIP.
2. Replace tests/ActionBridge.Computers.Tests/Program.cs in your repository with the included file. Keep the folder path exactly as shown.
3. Commit the change to main. The Build Android, Windows and Ubuntu workflow runs automatically. Simply rerunning the old failed commit will keep using the old file.
4. Alternatively apply actionbridge-ci-fix.patch using git apply, then commit and push.

The corrected suite passed twice locally: 22 checks passed, 0 failed. Windows/GitHub verification requires a new Actions run after applying this file.

Base commit: c46c81c05b64bc70127d135b8cc4b106a9adf91d
