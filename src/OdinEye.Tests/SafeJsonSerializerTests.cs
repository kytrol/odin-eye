namespace OdinEye.Tests
{
    using NUnit.Framework;
    using OdinEye.Http;
    using OdinEye.Models.Api;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;

    [TestFixture]
    public class SafeJsonSerializerTests
    {
        public class Fresh01 { public int A { get; set; } = 1; }
        public class Fresh02 { public int A { get; set; } = 2; }
        public class Fresh03 { public int A { get; set; } = 3; }
        public class Fresh04 { public int A { get; set; } = 4; }
        public class Fresh05 { public int A { get; set; } = 5; }
        public class Fresh06 { public int A { get; set; } = 6; }
        public class Fresh07 { public int A { get; set; } = 7; }
        public class Fresh08 { public int A { get; set; } = 8; }
        public class Fresh09 { public int A { get; set; } = 9; }
        public class Fresh10 { public int A { get; set; } = 10; }
        public class Fresh11 { public int A { get; set; } = 11; }
        public class Fresh12 { public int A { get; set; } = 12; }

        [Test]
        public void Serialize_FirstUseOfManyTypesInParallel_EveryCallSucceeds()
        {
            var calls = new Func<string>[]
            {
                () => Json(new Fresh01()), () => Json(new Fresh02()), () => Json(new Fresh03()),
                () => Json(new Fresh04()), () => Json(new Fresh05()), () => Json(new Fresh06()),
                () => Json(new Fresh07()), () => Json(new Fresh08()), () => Json(new Fresh09()),
                () => Json(new Fresh10()), () => Json(new Fresh11()), () => Json(new Fresh12()),
            };

            using (var start = new Barrier(calls.Length))
            {
                var errors = new ConcurrentBag<Exception>();
                var results = new string[calls.Length];
                var threads = calls.Select((call, i) => new Thread(() =>
                {
                    start.SignalAndWait();

                    try
                    {
                        results[i] = call();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                })).ToList();

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                Assert.That(errors, Is.Empty);
                Assert.That(results, Is.EqualTo(Enumerable.Range(1, calls.Length).Select(n => $"{{\"A\":{n}}}")));
            }
        }

        [Test]
        public void Warm_DoesNotThrow()
        {
            Assert.DoesNotThrow(SafeJsonSerializer.Warm);
        }

        [Test]
        public void Deserialize_ReadsTheWholeStream()
        {
            var body = Encoding.UTF8.GetBytes("{\"Stats\":{\"Deaths\":3}}");

            var submission = SafeJsonSerializer.Deserialize<CharacterStatsSubmission>(new OneByteAtATimeStream(body));

            Assert.That(submission.Stats, Is.EqualTo(new Dictionary<string, float> { { "Deaths", 3f } }));
        }

        private static string Json<T>(T value) => Encoding.UTF8.GetString(SafeJsonSerializer.Serialize(value));

        private class OneByteAtATimeStream : MemoryStream
        {
            public OneByteAtATimeStream(byte[] buffer) : base(buffer)
            {
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                base.Read(buffer, offset, Math.Min(count, 1));
        }
    }
}
