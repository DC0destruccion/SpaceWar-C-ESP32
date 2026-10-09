namespace NebulaInvaders.Core;


/// Separar esta lógica permite probar colisiones y niveles sin abrir la interfaz.

public sealed class GameEngine
{
    public const float WorldWidth = 960f;
    public const float WorldHeight = 640f;
    public const float PlayerWidth = 46f;
    public const float PlayerHeight = 30f;
    // Cada actualización se divide en pasos pequeños para no atravesar enemigos con las balas.
    private const float FixedStep = 1f / 120f;
    // Un disparo cada 0.25 segundos elimina la necesidad de un botón físico de disparo.
    private const float AutoFireInterval = 0.25f;
    private const float PlayerBulletSpeed = 550f;
    private const float HorizontalMargin = 32f;
    private const int InitialEnemyCount = 32;

    private readonly int _seed;
    private Random _random;
    private readonly List<Enemy> _enemies = [];
    private readonly List<Projectile> _projectiles = [];
    private readonly List<Particle> _particles = [];
    private float _direction = 1f;
    private float _playerShotTimer;
    private float _enemyShotTimer;
    private GamePhase _phaseBeforePause;

    public GamePhase Phase { get; private set; } = GamePhase.Ready;
    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; } = 3;
    /// <summary>Segundos de juego activo; excluye menús, introducciones y pausas.</summary>
    public float ElapsedSeconds { get; private set; }
    public float PlayerX { get; private set; } = WorldWidth / 2f;
    public float PlayerY => WorldHeight - 54f;
    public float InvulnerabilityRemaining { get; private set; }
    public float LevelIntroRemaining { get; private set; }
    public IReadOnlyList<Enemy> Enemies { get; }
    public IReadOnlyList<Projectile> Projectiles { get; }
    public IReadOnlyList<Particle> Particles { get; }
    public event Action<GameEvent>? EventRaised;

    // Solo el proyecto de pruebas puede preparar escenarios mediante estas listas modificables.
    // La interfaz recibe las vistas de solo lectura, para no alterar la simulación al dibujar.
    internal List<Enemy> MutableEnemies => _enemies;
    internal List<Projectile> MutableProjectiles => _projectiles;

    /// <summary>Una semilla fija reproduce el mismo azar durante las pruebas.</summary>
    public GameEngine(int? seed = null)
    {
        _seed = seed ?? Random.Shared.Next();
        _random = new Random(_seed);
        Enemies = _enemies.AsReadOnly();
        Projectiles = _projectiles.AsReadOnly();
        Particles = _particles.AsReadOnly();
    }

    /// <summary>Inicia una misión desde el nivel 1, con tres vidas y puntuación cero.</summary>
    public void Start()
    {
        _random = new Random(_seed);
        Level = 1;
        Score = 0;
        Lives = 3;
        ElapsedSeconds = 0;
        PlayerX = WorldWidth / 2f;
        InvulnerabilityRemaining = 0;
        _particles.Clear();
        BeginLevel();
    }

    /// <summary>Guarda la fase previa para reanudar tanto la introducción como el combate.</summary>
    public void TogglePause()
    {
        if (Phase == GamePhase.Paused)
            Phase = _phaseBeforePause;
        else if (Phase is GamePhase.Playing or GamePhase.LevelIntro)
        {
            _phaseBeforePause = Phase;
            Phase = GamePhase.Paused;
        }
    }

    /// <summary>
    /// Avanza como máximo 0.1 segundos por llamada, repartidos en subpasos.
    /// El límite evita grandes saltos si Windows tarda en entregar un fotograma.
    /// La entrada ya está calibrada: 0 es el borde izquierdo y 1 el derecho.
    /// </summary>
    /// <param name="dt">Tiempo transcurrido desde el fotograma anterior, en segundos.</param>
    /// <param name="normalizedInput">Posición absoluta solicitada por el potenciómetro o teclado.</param>
    public void Update(float dt, float normalizedInput)
    {
        if (!float.IsFinite(dt) || dt <= 0 || Phase is GamePhase.Ready or GamePhase.Paused)
            return;
        normalizedInput = float.IsFinite(normalizedInput)
            ? Math.Clamp(normalizedInput, 0f, 1f)
            : (PlayerX - HorizontalMargin) / (WorldWidth - 2 * HorizontalMargin);
        float remaining = Math.Min(dt, 0.1f);
        while (remaining > 0.000001f)
        {
            float step = Math.Min(remaining, FixedStep);
            Step(step, normalizedInput);
            remaining -= step;
        }
    }

    /// <summary>Ejecuta, en orden, movimiento, disparos, colisiones y cambio de nivel.</summary>
    private void Step(float dt, float input)
    {
        UpdateParticles(dt);
        if (Phase is GamePhase.GameOver or GamePhase.Won)
            return;
        if (Phase == GamePhase.LevelIntro)
        {
            LevelIntroRemaining = Math.Max(0, LevelIntroRemaining - dt);
            if (LevelIntroRemaining <= 0.00001f)
            {
                LevelIntroRemaining = 0;
                Phase = GamePhase.Playing;
            }
            return;
        }
        if (Phase != GamePhase.Playing)
            return;

        ElapsedSeconds += dt;
        InvulnerabilityRemaining = Math.Max(0, InvulnerabilityRemaining - dt);
        float targetX = HorizontalMargin + input * (WorldWidth - 2 * HorizontalMargin);
        // Interpolación exponencial: amortigua pequeñas variaciones del ADC y mantiene
        // una respuesta similar aunque cambie la cantidad de fotogramas por segundo.
        PlayerX = Math.Clamp(PlayerX + (targetX - PlayerX) * (1f - MathF.Exp(-12f * dt)),
            HorizontalMargin, WorldWidth - HorizontalMargin);

        MoveEnemies(dt);
        if (Phase != GamePhase.Playing)
            return;

        _playerShotTimer -= dt;
        if (_playerShotTimer <= 0)
        {
            _playerShotTimer += AutoFireInterval;
            _projectiles.Add(new Projectile(PlayerX, PlayerY - PlayerHeight / 2f - 10f,
                true, -PlayerBulletSpeed));
            EventRaised?.Invoke(GameEvent.Shot);
        }

        _enemyShotTimer -= dt;
        if (_enemyShotTimer <= 0 && _enemies.Count > 0)
        {
            _enemyShotTimer += LevelCatalog.All[Level - 1].EnemyShotInterval;
            FireEnemyProjectile();
        }

        MoveProjectilesAndResolveHits(dt);
        if (Phase == GamePhase.Playing && _enemies.Count == 0)
            CompleteLevel();
    }

    /// <summary>Crea cuatro filas de ocho enemigos y limpia disparos del nivel anterior.</summary>
    private void BeginLevel()
    {
        _enemies.Clear();
        _projectiles.Clear();
        _particles.Clear();
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 8; column++)
                _enemies.Add(new Enemy(210f + column * 77f, 102f + row * 48f, row, column));
        _direction = 1f;
        _playerShotTimer = 0;
        _enemyShotTimer = LevelCatalog.All[Level - 1].EnemyShotInterval;
        InvulnerabilityRemaining = 0;
        LevelIntroRemaining = 2f;
        Phase = GamePhase.LevelIntro;
        EventRaised?.Invoke(GameEvent.LevelStarted);
    }

    /// <summary>Desplaza la formación; al tocar un borde invierte su dirección y desciende.</summary>
    private void MoveEnemies(float dt)
    {
        if (_enemies.Count == 0)
            return;
        var definition = LevelCatalog.All[Level - 1];
        // Además de la dificultad de cada nivel, los últimos enemigos se mueven más rápido.
        float speed = definition.EnemySpeed * (1f + 0.65f * (1f - _enemies.Count / (float)InitialEnemyCount));
        float dx = _direction * speed * dt;
        float minLeft = float.MaxValue;
        float maxRight = float.MinValue;
        foreach (Enemy enemy in _enemies)
        {
            enemy.X += dx;
            minLeft = Math.Min(minLeft, enemy.X - enemy.Width / 2f);
            maxRight = Math.Max(maxRight, enemy.X + enemy.Width / 2f);
        }
        float correction = 0;
        bool bounce = false;
        if (_direction > 0 && maxRight >= WorldWidth - HorizontalMargin)
        {
            correction = WorldWidth - HorizontalMargin - maxRight;
            bounce = true;
        }
        else if (_direction < 0 && minLeft <= HorizontalMargin)
        {
            correction = HorizontalMargin - minLeft;
            bounce = true;
        }
        foreach (Enemy enemy in _enemies)
        {
            if (bounce)
            {
                enemy.X += correction;
                enemy.Y += 18f;
            }
            if (enemy.Y + enemy.Height / 2f >= PlayerY - PlayerHeight / 2f)
            {
                Finish(GamePhase.GameOver, GameEvent.Defeat);
                return;
            }
        }
        if (bounce)
            _direction *= -1;
    }

    private void FireEnemyProjectile()
    {
        // Solo dispara el enemigo inferior de cada columna ocupada: sus compañeros
        // no bloquean visualmente el origen del disparo. Luego elegimos una columna al azar.
        Enemy?[] bottomByColumn = new Enemy?[8];
        foreach (Enemy enemy in _enemies)
            if (bottomByColumn[enemy.Column] is not { } current || enemy.Y > current.Y)
                bottomByColumn[enemy.Column] = enemy;
        Enemy[] candidates = bottomByColumn.Where(enemy => enemy is not null).Cast<Enemy>().ToArray();
        Enemy shooter = candidates[_random.Next(candidates.Length)];
        _projectiles.Add(new Projectile(shooter.X, shooter.Y + shooter.Height / 2f + 8f,
            false, LevelCatalog.All[Level - 1].EnemyBulletSpeed));
    }

    /// Actualiza balas y aplica impactos; cada bala puede destruir un solo enemigo.
    private void MoveProjectilesAndResolveHits(float dt)
    {
        // Recorrer hacia atrás permite RemoveAt sin saltarse el siguiente elemento.
        for (int index = _projectiles.Count - 1; index >= 0; index--)
        {
            Projectile projectile = _projectiles[index];
            projectile.Y += projectile.VelocityY * dt;
            bool remove = projectile.Y < -24f || projectile.Y > WorldHeight + 24f;
            if (projectile.FromPlayer)
            {
                for (int enemyIndex = _enemies.Count - 1; enemyIndex >= 0 && !remove; enemyIndex--)
                {
                    Enemy enemy = _enemies[enemyIndex];
                    if (!Overlaps(projectile.X, projectile.Y, projectile.Width, projectile.Height,
                        enemy.X, enemy.Y, enemy.Width, enemy.Height))
                        continue;
                    _enemies.RemoveAt(enemyIndex);
                    Score += (4 - enemy.Row) * 10;
                    SpawnBurst(enemy.X, enemy.Y, 15, enemy.Type);
                    remove = true;
                    EventRaised?.Invoke(GameEvent.EnemyHit);
                }
            }
            else if (!remove && Overlaps(projectile.X, projectile.Y, projectile.Width, projectile.Height,
                PlayerX, PlayerY, PlayerWidth, PlayerHeight))
            {
                // La bala desaparece incluso con escudo. Durante 1.6 s no se descuentan
                // más vidas, evitando perder las tres por impactos casi simultáneos.
                remove = true;
                if (InvulnerabilityRemaining <= 0)
                {
                    Lives--;
                    InvulnerabilityRemaining = 1.6f;
                    SpawnBurst(PlayerX, PlayerY, 28, 3);
                    EventRaised?.Invoke(GameEvent.PlayerHit);
                    if (Lives <= 0)
                    {
                        Finish(GamePhase.GameOver, GameEvent.Defeat);
                        return;
                    }
                }
            }
            if (remove)
                _projectiles.RemoveAt(index);
        }
    }

    /// Entrega el bono del nivel y decide si comienza el siguiente o hay victoria.
    private void CompleteLevel()
    {
        Score += Level * 250;
        if (Level == LevelCatalog.All.Count)
        {
            SpawnBurst(WorldWidth / 2f, WorldHeight / 2f, 90, 1);
            Finish(GamePhase.Won, GameEvent.Victory);
        }
        else
        {
            Level++;
            BeginLevel();
        }
    }

    /// <summary>Detiene el combate y notifica el resultado, sin escribir en la base de datos.</summary>
    private void Finish(GamePhase phase, GameEvent gameEvent)
    {
        Phase = phase;
        _projectiles.Clear();
        LevelIntroRemaining = 0;
        EventRaised?.Invoke(gameEvent);
    }

    /// <summary>Genera partículas radiales con vida corta; el límite evita crecimiento ilimitado.</summary>
    private void SpawnBurst(float x, float y, int count, int colorIndex)
    {
        for (int i = 0; i < count && _particles.Count < 512; i++)
        {
            float angle = _random.NextSingle() * MathF.Tau;
            float speed = 35f + _random.NextSingle() * 145f;
            float life = 0.25f + _random.NextSingle() * 0.50f;
            _particles.Add(new Particle(x, y, MathF.Cos(angle) * speed,
                MathF.Sin(angle) * speed, life, colorIndex));
        }
    }

    /// <summary>Mueve los fragmentos de explosión y elimina los que agotaron su vida.</summary>
    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle particle = _particles[i];
            particle.Life -= dt;
            if (particle.Life <= 0)
                _particles.RemoveAt(i);
            else
            {
                particle.X += particle.VelocityX * dt;
                particle.Y += particle.VelocityY * dt;
            }
        }
    }

    // Colisión entre rectángulos alineados: compara distancias entre sus centros
    // con la suma de sus semianchos y semialtos. No requiere una biblioteca de física.
    private static bool Overlaps(float ax, float ay, float aw, float ah,
        float bx, float by, float bw, float bh)
        => Math.Abs(ax - bx) * 2f <= aw + bw && Math.Abs(ay - by) * 2f <= ah + bh;
}
