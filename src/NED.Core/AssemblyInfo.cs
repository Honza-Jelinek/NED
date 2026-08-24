using NED.Abstractions;

// Vestavěné uzly editoru (Output, Input). Jejich manifest je embedded resource,
// generovaný stejným nástrojem jako externí packy — jedna cesta kódu.
[assembly: NodePack("ned", Name = "NED built-in nodes", Version = "1.0.0")]
