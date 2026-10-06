# client — Unity project

Not created yet. To create it:

1. Unity Hub → New project → Unity 6 (6000.x), template **Universal 3D (URP)**, location `D:\automatic\client`.
2. Add the shared battle core to `client/Packages/manifest.json`:

   ```json
   "com.automatic.battle.core": "file:../../src/Battle.Core"
   ```

3. Game code lives in its own asmdef that references `Automatic.Battle.Core`. Rendering code must
   never be referenced from the core (the core asmdef has `noEngineReferences: true`).

See `design/01-sync-architecture.md` for the client's role (presentation + local re-simulation)
and `design/04-art-direction.md` for rendering direction.
