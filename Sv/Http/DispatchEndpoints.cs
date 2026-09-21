using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Sv.Configuration;

namespace Sv.Http;

public static class DispatchEndpoints
{
    public static void MapDispatchEndpoints(this WebApplication app)
    {
        app.MapMethods("/server_list_android.txt", [HttpMethods.Get, HttpMethods.Head], () => Results.Text(ServerList(Config.Server), "text/plain", Encoding.UTF8));
        app.MapMethods("/announcement_android", [HttpMethods.Get, HttpMethods.Head], AnnouncementResult);
        app.MapMethods("/announcement_other", [HttpMethods.Get, HttpMethods.Head], AnnouncementResult);
        app.MapAccountEndpoints();
    }

    private static IResult AnnouncementResult() => Results.Text(
        Announcement(Config.Server), "text/plain", Encoding.UTF8);

    public static string ServerList(ServerOptions options)
    {
        string address = $"{options.AdvertiseHost}:{options.GatewayPort}";
        // GateData 消费九列，ServerData 消费六列；分隔符必须是 LF。
        return $"server{options.HostId} 1 3 0 {options.ServerName} {options.ServerName} {address} {address} {address}\n"
            + "########\n"
            + $"{options.ServerId} {options.HostId} {options.ServerName} 1 1 1\n#network=tel\n";
    }

    public static string Announcement(ServerOptions options)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using CsvWriter csv = new(output, new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" });
        string[][] rows =
        [
            ["编号", "页签名称", "页面类型", "公告内容", "热门活动", "日常活动", "标题", "文字内容",
                "指向活动", "配图位置1", "配图位置2", "配图位置3", "是否外放", "hot标签"],
            ["1", options.AnnouncementTitle, "1", options.AnnouncementText, "", "",
                options.AnnouncementTitle, "", "", "", "", "", "1", "0"],
        ];
        foreach (string[] row in rows)
        {
            foreach (string field in row)
            {
                csv.WriteField(field);
            }
            csv.NextRecord();
        }
        csv.Flush();
        return output.ToString();
    }
}
