using NebulaInvaders.Core;

namespace NebulaInvaders.Checks;

/// <summary>Prepara situaciones reproducibles para comprobar colisiones, vidas y progresión completa.</summary>
internal static class CoreChecks
{
    public static void Run()
    {
        (string Name, Action Test)[] tests =
        [
            ("Cinco niveles con dificultad creciente", DifficultyIncreases),
            ("Inicio e introducción pausables", IntroAndPause),
            ("Movimiento suave y límites del potenciómetro", MovementAndInputBounds),
            ("Disparo automático y pausa completa", AutoFireAndPause),
            ("Impacto real, puntuación y partículas", EnemyCollision),
            ("Escudo temporal, tres vidas y derrota", PlayerCollisionAndGameOver),
            ("Derrota por invasión", Invasion),
            ("Cinco niveles, victoria, limpieza y reinicio", ProgressionAndRestart),
            ("Repetición determinista con semilla", Determinism),
            ("Subpasos, fotogramas lentos y entradas inválidas", BoundedUpdates),
            ("Rebote horizontal y descenso de la formación", FormationBounce)
        ];
        foreach (var (name, test) in tests)
        {
            test();
            Console.WriteLine($"[OK] {name}");
        }
    }

    private static void DifficultyIncreases()
    {
        Equal(5, LevelCatalog.All.Count, "Número de niveles");
        for (int index = 0; index < LevelCatalog.All.Count; index++)
        {
            LevelDefinition level = LevelCatalog.All[index];
            Equal(index + 1, level.Number, "Numeración consecutiva");
            True(!string.IsNullOrWhiteSpace(level.Name), "Nombre de nivel");
            if (index == 0) continue;
            LevelDefinition previous = LevelCatalog.All[index - 1];
            True(level.EnemySpeed > previous.EnemySpeed, "Velocidad de formación creciente");
            True(level.EnemyBulletSpeed > previous.EnemyBulletSpeed, "Velocidad de proyectil creciente");
            True(level.EnemyShotInterval < previous.EnemyShotInterval, "Frecuencia de disparo creciente");
        }
    }

    private static void IntroAndPause()
    {
        var engine = new GameEngine(7);
        engine.Update(0.1f, 0);
        Equal(GamePhase.Ready, engine.Phase, "No se inicia sin Start");
        engine.Start();
        Equal(GamePhase.LevelIntro, engine.Phase, "Introducción inicial");
        Equal(32, engine.Enemies.Count, "Formación 8 x 4");
        Equal(3, engine.Lives, "Vidas iniciales");
        float x = engine.Enemies[0].X;
        Advance(engine, 0.5f, 0);
        Near(x, engine.Enemies[0].X, "Enemigos congelados durante introducción");
        Near(480f, engine.PlayerX, "Jugador congelado durante introducción");
        Near(0f, engine.ElapsedSeconds, "Introducción excluida del tiempo activo");
        engine.TogglePause();
        float remaining = engine.LevelIntroRemaining;
        Advance(engine, 2f);
        Near(remaining, engine.LevelIntroRemaining, "La pausa conserva el contador");
        engine.TogglePause();
        Equal(GamePhase.LevelIntro, engine.Phase, "Reanudar introducción");
        EnterPlaying(engine);
    }

    private static void MovementAndInputBounds()
    {
        var engine = Playing();
        float start = engine.PlayerX;
        engine.Update(0.01f, 0);
        True(engine.PlayerX < start && engine.PlayerX > 32f, "Movimiento gradual");
        Advance(engine, 2f, -50);
        Near(32f, engine.PlayerX, "Tope izquierdo", 0.01f);
        Advance(engine, 2f, 50);
        Near(GameEngine.WorldWidth - 32f, engine.PlayerX, "Tope derecho", 0.01f);
        True(engine.PlayerX + GameEngine.PlayerWidth / 2 < GameEngine.WorldWidth, "Nave dentro del mundo");
        float x = engine.PlayerX;
        engine.Update(0.05f, float.NaN);
        Near(x, engine.PlayerX, "Entrada NaN conserva posición");
    }

    private static void AutoFireAndPause()
    {
        var engine = Playing();
        int shots = 0;
        engine.EventRaised += gameEvent => { if (gameEvent == GameEvent.Shot) shots++; };
        Advance(engine, 0.99f);
        Equal(4, shots, "Cuatro disparos en el primer segundo");
        engine.TogglePause();
        float elapsed = engine.ElapsedSeconds;
        float playerX = engine.PlayerX;
        float[] enemyX = engine.Enemies.Select(enemy => enemy.X).ToArray();
        float[] projectileY = engine.Projectiles.Select(projectile => projectile.Y).ToArray();
        Advance(engine, 3, 0);
        Near(elapsed, engine.ElapsedSeconds, "Tiempo congelado");
        Near(playerX, engine.PlayerX, "Jugador congelado");
        True(enemyX.SequenceEqual(engine.Enemies.Select(enemy => enemy.X)), "Enemigos congelados");
        True(projectileY.SequenceEqual(engine.Projectiles.Select(projectile => projectile.Y)), "Balas congeladas");
        Equal(4, shots, "Sin disparos en pausa");
        engine.TogglePause();
        Equal(GamePhase.Playing, engine.Phase, "Reanudar partida");
        engine.Update(0.05f, 0);
        True(engine.ElapsedSeconds > elapsed && engine.PlayerX < playerX, "Partida reanudada");
    }

    private static void EnemyCollision()
    {
        var engine = Playing();
        engine.MutableEnemies.Clear();
        engine.MutableEnemies.Add(new Enemy(480, 400, 0, 0));
        engine.MutableEnemies.Add(new Enemy(100, 100, 3, 1));
        engine.MutableProjectiles.Add(new Projectile(480, 450, true, -550));
        int hits = 0;
        engine.EventRaised += gameEvent => { if (gameEvent == GameEvent.EnemyHit) hits++; };
        engine.Update(0.1f, 0.5f);
        Equal(1, engine.Enemies.Count, "El impacto elimina un enemigo");
        Equal(40, engine.Score, "Fila superior vale 40");
        Equal(1, hits, "Un evento por impacto");
        True(engine.Particles.Count > 0, "Explosión visible");
        True(engine.Projectiles.All(projectile => projectile.Y > 450 || !projectile.FromPlayer),
            "La bala que impacta se consume");
    }

    private static void PlayerCollisionAndGameOver()
    {
        var engine = Playing();
        engine.MutableEnemies.Clear();
        engine.MutableEnemies.Add(new Enemy(100, 100, 0, 0));
        int defeats = 0;
        int hits = 0;
        engine.EventRaised += gameEvent =>
        {
            if (gameEvent == GameEvent.Defeat) defeats++;
            if (gameEvent == GameEvent.PlayerHit) hits++;
        };
        for (int i = 0; i < 3; i++)
            engine.MutableProjectiles.Add(new Projectile(engine.PlayerX, engine.PlayerY, false, 190));
        engine.Update(0.01f, 0.5f);
        Equal(2, engine.Lives, "Varios impactos simultáneos consumen una vida");
        True(engine.InvulnerabilityRemaining > 1.5f, "Se activa el escudo");
        Equal(1, hits, "Un evento de daño durante el escudo");
        for (int life = 1; life >= 0; life--)
        {
            Advance(engine, 1.7f);
            engine.MutableProjectiles.Clear();
            engine.MutableProjectiles.Add(new Projectile(engine.PlayerX, engine.PlayerY, false, 190));
            engine.Update(0.01f, 0.5f);
            Equal(life, engine.Lives, "Daño tras expirar el escudo");
        }
        Equal(GamePhase.GameOver, engine.Phase, "Derrota sin vidas");
        Equal(1, defeats, "Evento final único");
        Equal(0, engine.Projectiles.Count, "Balas limpiadas al finalizar");
        float elapsed = engine.ElapsedSeconds;
        Advance(engine, 1f);
        Near(elapsed, engine.ElapsedSeconds, "Tiempo detenido al finalizar");
        Equal(1, defeats, "Sin eventos finales duplicados");
    }

    private static void Invasion()
    {
        var engine = Playing();
        engine.MutableEnemies.Clear();
        engine.MutableEnemies.Add(new Enemy(100, engine.PlayerY - 10, 0, 0));
        engine.Update(0.01f, 0.5f);
        Equal(GamePhase.GameOver, engine.Phase, "Invasión termina la partida");
        Equal(3, engine.Lives, "La invasión no depende de agotar vidas");
    }

    private static void ProgressionAndRestart()
    {
        var engine = new GameEngine(11);
        int starts = 0;
        int victories = 0;
        engine.EventRaised += gameEvent =>
        {
            if (gameEvent == GameEvent.LevelStarted) starts++;
            if (gameEvent == GameEvent.Victory) victories++;
        };
        engine.Start();
        for (int level = 1; level <= 5; level++)
        {
            EnterPlaying(engine);
            Equal(level, engine.Level, "Nivel esperado");
            engine.MutableEnemies.Clear();
            engine.MutableEnemies.Add(new Enemy(480, 300, 3, 0));
            engine.MutableProjectiles.Add(new Projectile(480, 300, true, 0));
            // An outstanding enemy bullet must not survive a level transition.
            engine.MutableProjectiles.Add(new Projectile(50, 420, false, 190));
            engine.Update(0.01f, 0.5f);
            Equal(0, engine.Projectiles.Count, "Sin proyectiles entre niveles");
            if (level < 5)
            {
                Equal(GamePhase.LevelIntro, engine.Phase, "Introducción siguiente nivel");
                Equal(32, engine.Enemies.Count, "Nueva formación completa");
            }
        }
        Equal(GamePhase.Won, engine.Phase, "Victoria al completar el quinto nivel");
        Equal(3800, engine.Score, "Puntos por impactos y cinco bonos");
        Equal(5, starts, "Cinco inicios de nivel");
        Equal(1, victories, "Una victoria");
        engine.TogglePause();
        Equal(GamePhase.Won, engine.Phase, "Final no pausable");
        Advance(engine, 1f);
        Equal(1, victories, "Victoria sin repetirse");
        engine.Start();
        Equal(GamePhase.LevelIntro, engine.Phase, "Reinicio vuelve a introducción");
        Equal(1, engine.Level, "Reinicio a nivel uno");
        Equal(0, engine.Score, "Reinicio limpia puntos");
        Equal(3, engine.Lives, "Reinicio recupera vidas");
        Near(0, engine.ElapsedSeconds, "Reinicio limpia tiempo");
        Near(480, engine.PlayerX, "Reinicio centra jugador");
        Equal(0, engine.Particles.Count, "Reinicio limpia partículas");
    }

    private static void Determinism()
    {
        var first = Playing(12345);
        var second = Playing(12345);
        for (int i = 0; i < 600; i++)
        {
            float input = (MathF.Sin(i * 0.03f) + 1f) / 2f;
            first.Update(1f / 60f, input);
            second.Update(1f / 60f, input);
        }
        Equal(first.Phase, second.Phase, "Fase reproducible");
        Equal(first.Score, second.Score, "Puntos reproducibles");
        Equal(first.Lives, second.Lives, "Vidas reproducibles");
        Near(first.PlayerX, second.PlayerX, "Posición reproducible");
        True(first.Projectiles.Select(bullet => (bullet.X, bullet.Y, bullet.FromPlayer))
            .SequenceEqual(second.Projectiles.Select(bullet => (bullet.X, bullet.Y, bullet.FromPlayer))),
            "Disparos enemigos y posiciones reproducibles");
        True(first.Enemies.Select(enemy => (enemy.X, enemy.Y, enemy.Row, enemy.Column))
            .SequenceEqual(second.Enemies.Select(enemy => (enemy.X, enemy.Y, enemy.Row, enemy.Column))),
            "Enemigos reproducibles");
    }

    private static void BoundedUpdates()
    {
        var engine = Playing();
        float elapsed = engine.ElapsedSeconds;
        engine.Update(float.PositiveInfinity, 0);
        engine.Update(float.NaN, 0);
        engine.Update(-1, 0);
        Near(elapsed, engine.ElapsedSeconds, "dt inválido se ignora");
        engine.Update(1000f, float.PositiveInfinity);
        Near(elapsed + 0.1f, engine.ElapsedSeconds, "Fotograma grande limitado a 0.1 segundos");
        True(float.IsFinite(engine.PlayerX), "Entrada infinita no corrompe la simulación");
    }

    private static void FormationBounce()
    {
        var engine = Playing();
        engine.MutableEnemies.Clear();
        var enemy = new Enemy(GameEngine.WorldWidth - 32f - 21f, 100, 0, 0);
        engine.MutableEnemies.Add(enemy);
        engine.Update(0.01f, 0.5f);
        Near(118, enemy.Y, "El rebote hace descender 18 unidades");
        True(enemy.X + enemy.Width / 2f <= GameEngine.WorldWidth - 32f, "El borde se respeta");
        float x = enemy.X;
        engine.Update(0.01f, 0.5f);
        True(enemy.X < x, "Dirección invertida");
        Near(118, enemy.Y, "No desciende dos veces en el mismo borde");
    }

    private static GameEngine Playing(int seed = 7)
    {
        var engine = new GameEngine(seed);
        engine.Start();
        EnterPlaying(engine);
        return engine;
    }

    private static void EnterPlaying(GameEngine engine)
    {
        for (int i = 0; i < 500 && engine.Phase == GamePhase.LevelIntro; i++)
            engine.Update(0.01f, 0.5f);
        Equal(GamePhase.Playing, engine.Phase, "Introducción termina a tiempo");
    }

    private static void Advance(GameEngine engine, float seconds, float input = 0.5f)
    {
        while (seconds > 0.00001f)
        {
            float dt = Math.Min(0.05f, seconds);
            engine.Update(dt, input);
            seconds -= dt;
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
        => True(EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message}: esperado {expected}, obtenido {actual}");

    private static void Near(float expected, float actual, string message, float tolerance = 0.001f)
        => True(Math.Abs(expected - actual) <= tolerance,
            $"{message}: esperado {expected}, obtenido {actual}");
}
