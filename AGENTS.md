# TajsToucher workspace conventions

- The user's permanent daily-use executable is `artifacts/publish/single-file/TajsToucher.exe`.
- Publish deliverable updates with `dotnet publish src/TajsToucher/TajsToucher.csproj -c Release -p:PublishProfile=SingleFile`. Do not change the user-facing output directory for each task.
- Isolated test publishes may use other artifact directories, but deliver the validated build to the permanent path and link that path in the final response.
- Do not terminate a running daily-use app or overwrite it while signing is active. If it locks the executable, ask the user to exit it before replacing the build.
- Preserve notification settings, Git configuration, and uninstall backup state during ordinary executable updates. Installation/configuration migration is separate from publishing.
