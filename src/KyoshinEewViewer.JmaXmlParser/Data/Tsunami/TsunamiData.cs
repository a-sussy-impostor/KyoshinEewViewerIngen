using U8Xml;

namespace KyoshinEewViewer.JmaXmlParser.Data.Tsunami;

public struct TsunamiData
{
	private XmlNode Node { get; set; }

	public TsunamiData(XmlNode node)
	{
		Node = node;
	}

	private TsunamiForecast? forecast = null;
	/// <summary>
	/// 津波の予測に関する情報
	/// </summary>
	public TsunamiForecast? Forecast => forecast ??= (Node.TryFindChild(Literals.Forecast(), out var n) ? new(n) : null);

}
