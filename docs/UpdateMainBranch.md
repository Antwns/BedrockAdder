# Updating the `main` branch with the latest converter fixes

The current fixes for rendering 3D model icons and recoloring 2D items live on the `work` branch. Use the following steps to bring those commits into your local `main` branch and ensure Visual Studio sees the updated files.

1. **Fetch the latest commits**

   ```bash
   git fetch origin
   ```

   This updates your local knowledge of the remote branches. Replace `origin` with the name of your remote if it differs.

2. **Switch to the `work` branch and make sure it is up to date**

   ```bash
   git checkout work
   git pull --ff-only origin work
   ```

   You should now have the latest renderer fixes locally. Visual Studio will show the refreshed file contents at this point.

3. **Return to `main`**

   ```bash
   git checkout main
   ```

4. **Merge the updated `work` branch into `main`**

   ```bash
   git merge --ff-only work
   ```

   The `--ff-only` flag keeps the history linear and avoids accidental merge commits. If Git reports that a fast-forward merge is not possible, make sure `work` contains the desired commits and retry.

5. **Verify the updated files**

   * Open the solution in Visual Studio (or reload the solution if it was already open).
   * Confirm that `BedrockAdder/Renderer/CefOffscreenIconRenderer.cs` references `CaptureScreenshotAsync()` and that `_icons` snapshots are generated when running the converter.

6. **Push your updated `main` branch**

   ```bash
   git push origin main
   ```

   After pushing, any other machines can simply `git pull origin main` to receive the fixes.

## Troubleshooting tips

- If Visual Studio still shows older file contents, close the solution and reopen it, or run `git status` to ensure there are no pending local changes masking the merge.
- To verify the specific commit is present, run `git log --oneline main | head -n 5` and look for the commit titled `Fix CefSharp screenshot invocation`.

