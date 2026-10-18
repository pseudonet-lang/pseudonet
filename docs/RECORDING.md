# Recording the demo GIF

A first version (`demo-tetris.gif`, 290 KB) was captured automatically from the Tetris sample: the window is
grabbed frame by frame with `PrintWindow` and no keyboard or mouse input, so it only shows the pieces falling by
themselves. A hand-played recording (below) would be nicer.

The README expects `docs/demo-tetris.gif` (and the site expects `assets/demo-tetris.gif`). Suggested recipe:

1. Install [ScreenToGif](https://www.screentogif.com) (free).
2. Create a project from the **Tetris** template in Visual Studio; show `Program.pseudo` for a few seconds
   (use a larger editor font), press **F5**, and play about 10 seconds.
3. Keep it under ~5 MB: 12-15 fps, crop to the game window, 480 px wide max.
4. Save to `docs/demo-tetris.gif`, and copy it to `../PseudoNet-Site/assets/`.

A second short clip showing the *same* program written in French is a strong complement for teachers.
Reusable capture script: `tools/record-gif.ps1 -Exe <app.exe> -Out <file.gif>`.
