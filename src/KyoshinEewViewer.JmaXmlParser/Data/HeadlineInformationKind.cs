using U8Xml;

namespace KyoshinEewViewer.JmaXmlParser.Data;

public struct Kind
{
	private XmlNode Node { get; set; }

	public Kind(XmlNode node)
	{
		Node = node;
	}

	private string? name = null;
	/// <summary>
	/// 事項種別名
	/// </summary>
	public string Name => name ??= (Node.TryFindStringNode(Literals.Name(), out var n) ? n : throw new JmaXmlParseException("Name ノードが存在しません"));

	private string? code = null;
	/// <summary>
	/// 事項種別コード<br/>
	/// 存在しない場合は <c>null</c>
	/// </summary>
	public string? Code => code ??= (Node.TryFindStringNode(Literals.Code(), out var n) ? n : null);

	private string? condition = null;
	/// <summary>
	/// 事項の状態<br/>
	/// 例: <c>土砂災害、浸水害</c><br/>
	/// 存在しない場合は <c>null</c>
	/// </summary>
	public string? Condition => condition ??= (Node.TryFindStringNode(Literals.Condition(), out var n) ? n : null);
}