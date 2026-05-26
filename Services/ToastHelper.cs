using System.Security;

namespace StreamCommand.Services;

/// <summary>
/// Shared Windows toast notification helper.
/// Centralises the XML-building boilerplate so no view needs its own copy.
/// </summary>
public static class ToastHelper
{
    public static void Show(string title, string body)
    {
        try
        {
            title = SecurityElement.Escape(title) ?? title;
            body  = SecurityElement.Escape(body)  ?? body;

            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml($"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{title}</text>
                      <text>{body}</text>
                    </binding>
                  </visual>
                </toast>
                """);
            var toast = new Windows.UI.Notifications.ToastNotification(xml);
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier().Show(toast);
        }
        catch { /* Toast not available until package identity is established */ }
    }
}
