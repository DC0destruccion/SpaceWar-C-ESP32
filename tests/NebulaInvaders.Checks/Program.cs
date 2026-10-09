using NebulaInvaders.Checks;

try
{
    // Estas comprobaciones se ejecutan desde consola y fallan con código de salida distinto de cero.
    // No necesitan una placa: prueban motor, mensajes recibidos y conservación local de resultados.
    CoreChecks.Run();
    ProtocolChecks.Run();
    await DataChecks.RunAsync();
    Console.WriteLine("Todas las comprobaciones terminaron correctamente.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"COMPROBACIÓN FALLIDA: {exception}");
    return 1;
}
