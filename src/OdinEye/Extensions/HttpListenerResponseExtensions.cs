namespace OdinEye.Extensions
{
    using Http;
    using System.Text;
    using WebSocketSharp.Net;

    public static class HttpListenerResponseExtensions
    {
        public static void Ok<TInstance>(this HttpListenerResponse response, TInstance instance)
        {
            var serialized = SafeJsonSerializer.Serialize(instance);

            response.StatusCode = 200;
            response.ContentType = "application/json";
            response.ContentLength64 = serialized.Length;
            response.ContentEncoding = Encoding.UTF8;
            response.OutputStream.Write(serialized, 0, serialized.Length);
            response.Close();
        }

        public static void Error(this HttpListenerResponse response, int statusCode = 500)
        {
            var body = Encoding.UTF8.GetBytes("An error occurred while handling this request.");

            response.StatusCode = statusCode;
            response.ContentType = "text/plain";
            response.ContentLength64 = body.Length;
            response.ContentEncoding = Encoding.UTF8;
            response.OutputStream.Write(body, 0, body.Length);
            response.Close();
        }
    }
}