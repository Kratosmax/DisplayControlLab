namespace MonitorInspector.Updates;
public static class UpdateTrust {
 public const string ProductId = "DisplayControlLab.Windows.Desktop";
 public const string Repository = "https://github.com/Kratosmax/DisplayControlLab";
 public const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAxvBtyYOBzGmK0wAs02ba
A6qpmWnhRop3mJuRVBqrK511+9FYWEVHegjP/XO9MAEwqzxTCxD6mtvLZe/gzbcN
xYz2ccv82FyZf1HiqGU3Z8LwQDRFTJFq+MXw5mO6kqSGa35iSkA8X8U1CFOI0J9a
z8iWm+a1Frnp1R45o8xr+ftfNMlRy+sKXUQq19sEJxVHfvDWOf8L1fWUiBZ8dnIK
jg45XxLdsXyvHs6DdX4V9XnlJl9jxoXBKuZOKF7y6RAWgrj3YP9Wmn2g9YimYceu
6ozBZFRJShN3E/LKAEndOk99hxd1jdo7z5esNDowwnDhnG1NBi9qWLYttjgLJDEi
ZNUvo09hdYT7/dIfxfL/nkR7PilQdcRcoMIaU4LwQkexOwvjxB3wkG1ghrvM6aWp
oPnnb+TUmSo1rtNo9NR3fRy4Mt+nWJr0cZ4ElLPYQhaCD2Ucb/fddD5wJ5E1clRE
CHcfkMlIV56R+UUEnfDt/u2IE5li4v1SAMLO4NzwvUm1AgMBAAE=
-----END PUBLIC KEY-----
""";
 public static bool IsSupportedChannel(string channel) => channel is "Lite" or "Full";
 public static string ManifestName(string channel) => channel switch { "Lite" => "update-lite.json", "Full" => "update-full.json", _ => throw new InvalidDataException("未知更新通道") };
}
