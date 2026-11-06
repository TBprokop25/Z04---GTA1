using Raylib_cs;
using Z04;

const int Width = 1280, Height = 720;

Raylib.InitWindow(Width, Height, "Z04 - Chaos City");
Raylib.SetTargetFPS(60);

var game = new Game(Width, Height);

while (!Raylib.WindowShouldClose())
{
    float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
    game.Update(dt);

    Raylib.BeginDrawing();
    game.Draw();
    Raylib.EndDrawing();
}

Raylib.CloseWindow();
