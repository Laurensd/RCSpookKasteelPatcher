namespace RedCatPatcher;

/// <summary>Fout met een melding die zo aan de gebruiker getoond kan worden.</summary>
public sealed class PatchException(string message) : Exception(message);
