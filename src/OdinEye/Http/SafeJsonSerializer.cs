namespace OdinEye.Http
{
    using Models.Api;
    using System.Collections.Generic;
    using System.IO;
    using Utf8Json;

    // One gate for every Utf8Json call: two concurrent first-use formatter builds sometimes crashed the server.
    public static class SafeJsonSerializer
    {
        private static readonly object Gate = new object();

        public static byte[] Serialize<T>(T value)
        {
            lock (Gate)
            {
                return JsonSerializer.Serialize(value);
            }
        }

        public static T Deserialize<T>(Stream stream)
        {
            byte[] body;
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                body = buffer.ToArray();
            }

            lock (Gate)
            {
                return JsonSerializer.Deserialize<T>(body);
            }
        }

        public static void Warm()
        {
            Serialize<IEnumerable<Player>>(new List<Player> { new Player() });
            Serialize(new ServerDetails());
            Serialize(new WorldDetails
            {
                WorldKeys = new List<string> { string.Empty },
                GlobalKeys = new List<GlobalKey> { new GlobalKey() },
            });

            Serialize(new BossDetails { Bosses = new List<Boss> { new Boss() } });
            Serialize(new WorldModifiersDetails { Modifiers = new List<WorldModifierValue> { new WorldModifierValue() } });
            Serialize(new AcceptedResponse());
            Serialize(new Dictionary<string, IReadOnlyDictionary<string, float>>
            {
                { string.Empty, new Dictionary<string, float> { { string.Empty, 0f } } },
            });

            Deserialize<CharacterStatsSubmission>(new MemoryStream(Serialize(new CharacterStatsSubmission
            {
                Stats = new Dictionary<string, float> { { string.Empty, 0f } },
            })));
        }
    }
}
