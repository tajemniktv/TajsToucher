# TajsToucher workspace conventions

- The user's permanent daily-use executable is `%LOCALAPPDATA%/Programs/TajemnikTV/TajsToucher/current/TajsToucher.exe`.
- Successful local app builds automatically publish and deploy there. `dotnet publish src/TajsToucher/TajsToucher.csproj -c Release -p:PublishProfile=SingleFile` also stages and deploys. Use `-p:DogfoodEnabled=false` for isolated builds/publishes (`Dogfood=false` remains compatible).
- Staging lives in `.codex/temp/dogfood`; `current`, `previous`, and `retained` live inside the app root, matching TajsTokens. Deliver the validated build to the permanent path and link that path in the final response.
- Both desktop apps use `tools/dogfood/Deploy.ps1` and `tools/dogfood/Test-Deployment.ps1` as their workflow entry points. Test deployment/migration changes in isolation before updating the daily build.
- Do not terminate a running daily-use app or overwrite it while signing is active. If it locks the executable, ask the user to exit it before replacing the build.
- Preserve notification settings, Git configuration, and uninstall backup state during ordinary executable updates. Installation/configuration migration is separate from publishing.
