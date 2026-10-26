using System;
using System.Net;
using System.Net.Sockets;
using System.Speech.Synthesis;
using System.Text;

namespace OperatorAccessibility
{
    internal static class OperatorSpeech
    {
        private static void Main()
        {
            using (UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 49091)))
            using (SpeechSynthesizer speech = new SpeechSynthesizer())
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
                while (true)
                {
                    byte[] data = server.Receive(ref sender);
                    string text = Encoding.UTF8.GetString(data);
                    if (text == "__EXIT__")
                        return;
                    if (String.IsNullOrWhiteSpace(text))
                        continue;
                    speech.SpeakAsyncCancelAll();
                    speech.SpeakAsync(text);
                }
            }
        }
    }
}
