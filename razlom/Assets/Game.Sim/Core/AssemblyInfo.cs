using System.Runtime.CompilerServices;

// В Unity тесты собраны отдельно: им нужен доступ к внутреннему режиму бессмертия.
[assembly: InternalsVisibleTo("Game.Tests")]
