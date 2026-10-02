using System;
using System.Collections.Generic;
using System.Text;

namespace NeuroSpeech.Smtp;

public class SocketClosedException: Exception
{
    public SocketClosedException(string message): base(message)
    {
        
    }
}
