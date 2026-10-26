using System.Runtime.CompilerServices;

// Tests reach the overlay's fields and the queue's directory override, neither
// of which belongs on the public surface.
[assembly: InternalsVisibleTo("Buildtable.Tests")]
