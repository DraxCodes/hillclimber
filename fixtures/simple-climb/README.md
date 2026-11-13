# simple-climb (fixture)

A deliberately tiny hill-climb target. It exists so the dashboard's climb loop can be
exercised end to end against a real .NET test project: `opencode run` makes a change,
`dotnet test` measures it, and the loop keeps or reverts.

The initial state is **half finished**: `Calculator.Add`/`Subtract` work, while
`Multiply`/`Divide`/`IsEven` throw `NotImplementedException`, so three of the six tests
fail. The goal prompt

> Implement the unimplemented methods in Calculator.cs so all tests pass.

converges in a single good iteration.

## Do not edit this copy in place

The climb loop `git checkout -- .` to revert losing iterations, so the target must be its
own git repository. Materialise a throwaway copy with:

```powershell
pwsh scripts/climb-fixture.ps1
```

The script copies this folder to a temp directory, runs `git init` and an initial commit,
and prints the absolute path to point the dashboard's "target folder" at. This repo's own
git history is never touched.
