using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NebulaInvaders.Checks")]

namespace NebulaInvaders.Core;

// Estados posibles de la misión. Evitan combinar banderas contradictorias como jugar y perder.
public enum GamePhase { Ready, Playing, Paused, LevelIntro, Won, GameOver }
// Notificaciones del motor para que otros componentes reaccionen sin acoplarse a su lógica.
public enum GameEvent { Shot, EnemyHit, PlayerHit, LevelStarted, Victory, Defeat }

/// <summary>Velocidades en unidades del escenario por segundo; intervalo de disparo en segundos.</summary>
public sealed record LevelDefinition(
    int Number, string Name, float EnemySpeed, float EnemyShotInterval, float EnemyBulletSpeed);

/// <summary>Modifica esta tabla para ajustar los cinco niveles sin cambiar el motor.</summary>
public static class LevelCatalog
{
    public static IReadOnlyList<LevelDefinition> All { get; } = new ReadOnlyCollection<LevelDefinition>(
    [
        new(1, "Primer contacto", 48f, 1.30f, 190f),
        new(2, "Órbita hostil", 68f, 1.08f, 225f),
        new(3, "Tormenta de plasma", 91f, 0.88f, 265f),
        new(4, "Asedio estelar", 120f, 0.70f, 310f),
        new(5, "Última frontera", 154f, 0.54f, 360f)
    ]);
}

/// <summary>Invasor de una fila y columna; solo el motor puede modificar su posición.</summary>
public sealed class Enemy
{
    public float X { get; internal set; }
    public float Y { get; internal set; }
    public float Width => 42f;
    public float Height => 30f;
    public int Row { get; }
    public int Column { get; }
    public int Type => Row % 3;

    internal Enemy(float x, float y, int row, int column)
        => (X, Y, Row, Column) = (x, y, row, column);
}

/// <summary>Una bala; FromPlayer distingue aliados y enemigos, y VelocityY marca el sentido.</summary>
public sealed class Projectile
{
    public float X { get; internal set; }
    public float Y { get; internal set; }
    public bool FromPlayer { get; }
    public float Width => FromPlayer ? 5f : 7f;
    public float Height => FromPlayer ? 18f : 14f;
    internal float VelocityY { get; }

    internal Projectile(float x, float y, bool fromPlayer, float velocityY)
        => (X, Y, FromPlayer, VelocityY) = (x, y, fromPlayer, velocityY);
}

/// <summary>Fragmento visual de explosión. Life / MaxLife determina su desvanecimiento.</summary>
public sealed class Particle
{
    public float X { get; internal set; }
    public float Y { get; internal set; }
    public float Life { get; internal set; }
    public float MaxLife { get; }
    public int ColorIndex { get; }
    internal float VelocityX { get; }
    internal float VelocityY { get; }

    internal Particle(float x, float y, float velocityX, float velocityY, float life, int colorIndex)
        => (X, Y, VelocityX, VelocityY, Life, MaxLife, ColorIndex)
            = (x, y, velocityX, velocityY, life, life, colorIndex);
}
