using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Models
{
    public class AppResult
    {
        public bool IsSuccess { get; private set; }
        public string Message { get; private set; }

        public static AppResult Success(string message = "") => new AppResult { IsSuccess = true, Message = message };
        public static AppResult Fail(string message) => new AppResult { IsSuccess = false, Message = message };
    }

}
