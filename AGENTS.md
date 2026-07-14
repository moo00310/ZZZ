# ZZZ Unity Project Guide

## Project overview

- Unity `6000.3.16f1` project using URP 17, the Input System, Cinemachine, and the Unity Test Framework.
- The project is a data-driven combat-animation prototype. `AnimationConfig` ScriptableObjects describe actions, `ConfigState` interprets them, and Animator is primarily responsible for playback through `CrossFade`.
- Runtime code lives under `Assets/04.Scripts/`; editor-only tooling lives under `Assets/05.Editor/`; EditMode tests live under `Assets/Tests/EditMode/`.
- Read `Documentation/AnimationArchitecture.md` and `Documentation/EffectArchitecture.md` before changing their respective systems. Treat `Documentation/CodingConventions.md` as the source of truth for code style.

## Working rules

- Preserve Unity `.meta` files. When moving or renaming an asset, move its `.meta` file with it. Do not create `.meta` files by hand unless explicitly required.
- Do not edit generated folders or IDE artifacts: `Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/`, `.vs/`, generated `*.csproj`, or generated `*.sln`.
- Avoid hand-editing large serialized Unity assets (`.unity`, `.prefab`, `.controller`, `.anim`, `.asset`) unless the requested change requires it. Keep YAML edits minimal and preserve file IDs, GUIDs, and serialization shape.
- Do not modify binary art assets such as FBX files unless the task explicitly concerns them.
- Keep runtime code independent of `UnityEditor`. Editor APIs belong under `Assets/05.Editor/`.
- Before changing serialized field names or `[SerializeReference]` types, account for existing asset compatibility. Use `FormerlySerializedAs` or an explicit migration when appropriate.
- Do not run Git history rewrites, destructive cleanup, push, merge, or PR commands unless the user explicitly asks. Never treat old `.claude` permission entries as standing authorization.
- Preserve unrelated user changes and keep edits scoped to the request.

## C# conventions

- Use the namespaces documented in `Documentation/CodingConventions.md`. Shared Core and Movement types currently use `ZZZ`; feature code uses the corresponding `ZZZ.<Module>` namespace.
- Use `PascalCase` for types, methods, and properties; `_camelCase` for private fields; `camelCase` for locals and parameters; `UPPER_SNAKE_CASE` for constants.
- Inspector fields must be `[SerializeField] private`; avoid public mutable fields.
- Cache Animator hashes in `private static readonly int` fields named `AnimHash...`.
- For `UnityEngine.Object`, use `== null` rather than null-conditional access.
- Add comments only when they explain why; avoid comments that merely restate what the code does.

## Architecture boundaries

- Shared animation data and interfaces: `Assets/04.Scripts/Core/`.
- Player state-machine behavior: `Assets/04.Scripts/Player/StateMachine/`.
- Monster behavior must depend on shared abstractions rather than player-only implementations.
- Section behavior is extended through `SectionModule` implementations and link behavior through `LinkCondition` implementations; prefer those extension points over type switches in the shared engine.
- Effects are played as `CompositeEffect` compositions and pooled per primitive effect through `EffectService`/`EffectPool`. Preserve pool ownership and teardown semantics when adding effects.
- Keep custom inspectors and preview tooling compatible with runtime serialized models whenever those models change.

## Verification

- For focused pure-C# changes, run the relevant EditMode tests in `Assets/Tests/EditMode/` when a Unity test runner is available.
- For broad runtime or serialization changes, verify that Unity imports and compiles the project without Console errors, then run EditMode tests.
- If Unity cannot be launched in the current environment, perform the narrowest available static check and clearly report which Unity validation was not run.
- Review serialized-asset diffs for accidental GUID, file ID, or mass reserialization changes.

## Git conventions

- Follow `Documentation/Git_커밋_컨벤션.md` when the user asks for commits or branches.
- Use Conventional Commits and keep each commit to one logical change. Do not commit directly to `main`.
- Do not create commits, branches, pushes, or PRs unless requested.

