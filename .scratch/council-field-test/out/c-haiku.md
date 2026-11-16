### 1. Risk posture

The trust boundary is process isolation: the app runs locally in a trusted user context; the subprocess (`opencode run`) is spawned with user-supplied prompts and folder paths; both share a JSON event stream over stdout. The transport is in-memory IPC and HTTP loopback (when PIN is real) or localhost-only (when PIN is default). 

What must never happen:
- A question response smuggled with injected JSON that breaks the event stream or corrupts stored transcript state.
- A malicious question option that exploits the rendering layer and exfiltrates session state or file contents.
- The prompt or question payload escaping the subprocess boundary and executing arbitrary code in the app's context.
- Session IDs or answers persisted unencrypted in transcript files, exposing decision history to attackers with filesystem access.

### 2. Three highest-value risks

**1. JSON injection via question deserialization** (highest impact).
Failure mode: `opencode run` sends a malformed or adversarial question event (option list with embedded newlines or null bytes); the app deserializes it without strict schema validation and injects it into the transcript. If the JSON parser is lenient, a crafted question could break frame boundaries or inject false events.
How it happens: `OpenCodeRun.cs` spawns `opencode` with `--format json`, but does not validate the event shape before rendering. The transcript is stored verbatim; corrupted events could later corrupt playback or leak data.
Constraint: All question events must pass strict schema validation (field count, type, null-byte filtering) before any part reaches the UI or storage. Validation gate in the deserialization layer, not the renderer.

**2. Session ID as a capability / eavesdropping vector** (second).
Failure mode: Two users on the same LAN both unlock ClimberWeb with their PINs; session IDs travel in cleartext (spec decision 2, Finding 4). A passive attacker captures a session ID and injects it into a second app instance to replay that user's runs or see their transcripts.
How it happens: The app treats the session ID as a shared capability token (spec, line 37). If the PIN gate is briefly open during startup or during a PIN reset, the session ID is visible. An attacker with network access and a captured session ID can impersonate that session.
Constraint: Session IDs must be cryptographically unique per app instance and invalidated on any PIN change or idle timeout. The gate must re-lock all active sessions if the PIN is reconfigured.

**3. Arbitrary stdio capture from `opencode` subprocess** (third).
Failure mode: `opencode run` outputs a question; the reader's answer is piped back to stdin. If the subprocess crashes or hangs, stdin remains open and the app buffers unbounded output. A malicious `opencode` plugin or corrupted run state could exhaust memory or block the thread reading stdin.
How it happens: `OpenCodeRun.cs` builds the command, but `CommandRequest` and the caller are not shown here. If the answer delivery does not include a timeout or size limit, a hung subprocess leaves the app waiting forever or consuming memory.
Constraint: Question/answer round-trip must have a configurable timeout (e.g., 30 seconds) and a maximum answer payload size. If the subprocess hangs, the run fails gracefully and the app rolls back the question state.

### 3. The one thing you would cut

**Cut free-text answers.** The proposal adds a reader's answer field alongside options. Free-text answers are unbounded, expose the reader's reasoning to storage and replay, and create a new injection vector if the app later uses that answer in a shell context or log. Options alone are sufficient for control flow; cutting free-text eliminates a storage + injection surface and simplifies the contract with `opencode` to a single validated choice per question.
