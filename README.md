# Z04 – Chaos City

2D hra ve stylu GTA 1 (pohled shora) v C# / .NET s knihovnou [Raylib-cs](https://github.com/raylib-cs/raylib-cs).
Žádný příběh – cílem je způsobovat chaos a vydělávat na něm.

## Spuštění

```bash
dotnet add package Raylib-cs
dotnet run
```

## Ovládání

| Klávesa | Akce |
|---|---|
| WASD / šipky | chůze, v autě plyn / brzda / zatáčení |
| Shift | běh (pěšky) |
| Space | ruční brzda (v autě) |
| E | nastoupit / vystoupit (nejbližší auto v dosahu) |
| Myš + LMB | míření a střelba (v tanku střílí kanon) |
| Q | přepnout vlastněnou zbraň |
| B | obchod se zbraněmi (1–5 = koupit / vybrat) |
| N | nový sidequest |

## Splnění zadání

| Požadavek | Realizace |
|---|---|
| 2D pohled shora | `Game.Draw`, kamera `Camera2D` sleduje hráče / auto |
| Jednoduché exploze | `Game.Explode` – plošné poškození, částice, třesení kamery, řetězové exploze aut |
| Ničení aut | HP vozidel, náraz do zdí / aut, střelba → hoření → výbuch → vrak (`VState`) |
| Jízda s vozidly | `Game.DriveVehicle` – akcelerace, zatáčení závislé na rychlosti, ruční brzda, nastupování |
| Policajti | `PKind.Cop` + policejní auta; chovají se podle počtu hvězdiček |
| Armáda | od 4 hvězdiček vojáci, na 5 hvězdiček tank (jde ukrást a řídit!) |
| Sidequesty | 4 typy: zničit auta, zabít policisty, dojet na bod v limitu, přežít na 3+ hvězdách |
| Lepší zbraně | pistole, SMG, brokovnice, minigun, raketomet (kupují se za peníze) |
| Bez většího děje | žádný příběh, otevřené město |
| Výdělek z chaosu | peníze za zničená auta, zabité osoby a sidequesty, násobič **chaos combo** (až ×5) |

## Struktura

- `Program.cs` – okno a herní smyčka
- `Entities.cs` – vozidla, osoby, střely, zbraně, paleta, klávesy
- `World.cs` – generované město, kolize, vykreslení mapy
- `Game.cs` – logika (hráč, AI, policie, exploze, questy) a vykreslení HUD
