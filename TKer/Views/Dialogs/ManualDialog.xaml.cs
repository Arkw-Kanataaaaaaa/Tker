using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TKer.Views.Dialogs;

public partial class ManualDialog : Window
{
    public ManualDialog()
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
        TocList.SelectedIndex = 0;
    }

    private void TocList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ContentPanel.Children.Clear();
        switch (TocList.SelectedIndex)
        {
            case 0: RenderHome();        break;
            case 1: RenderCreateProject(); break;
            case 2: RenderMultiProject(); break;
            case 3: RenderCategory();    break;
            case 4: RenderTask();        break;
            case 5: RenderFolder();      break;
            case 6: RenderGantt();       break;
            case 7: RenderCalendar();    break;
            case 8: RenderDeliverable(); break;
            case 9: RenderFaq();         break;
        }
        ContentScroll.ScrollToTop();
    }

    // ── ヘルパー ──────────────────────────────────
    private void H1(string text) => ContentPanel.Children.Add(new TextBlock
    {
        Text = text, FontFamily = new FontFamily("Yu Gothic UI"), FontWeight = FontWeights.Black,
        FontSize = 20, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 16)
    });
    private void H2(string text) => ContentPanel.Children.Add(new TextBlock
    {
        Text = text, FontFamily = new FontFamily("Yu Gothic UI"), FontWeight = FontWeights.Bold,
        FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(61,126,255)),
        Margin = new Thickness(0, 16, 0, 8)
    });
    private void P(string text) => ContentPanel.Children.Add(new TextBlock
    {
        Text = text, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(200,206,220)),
        TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 0, 0, 10)
    });
    private void Tip(string text) => ContentPanel.Children.Add(new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(40,61,126,255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(120,61,126,255)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,0,0,10),
        Child = new TextBlock { Text = "💡 " + text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(136,180,255)) }
    });
    private void Warn(string text) => ContentPanel.Children.Add(new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(40,255,107,53)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(120,255,107,53)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,0,0,10),
        Child = new TextBlock { Text = "⚠️ " + text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(255,180,100)) }
    });
    private void Step(int n, string text) => ContentPanel.Children.Add(new StackPanel
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,6),
        Children =
        {
            new Border { Width=24, Height=24, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(61,126,255)),
                Child = new TextBlock { Text = n.ToString(), FontSize=12, FontWeight=FontWeights.Bold,
                    Foreground=Brushes.White, HorizontalAlignment=HorizontalAlignment.Center,
                    VerticalAlignment=VerticalAlignment.Center } },
            new TextBlock { Text = "  " + text, FontSize=13, VerticalAlignment=VerticalAlignment.Center,
                Foreground=new SolidColorBrush(Color.FromRgb(200,206,220)),
                TextWrapping=TextWrapping.Wrap, MaxWidth=480 }
        }
    });
    private void Sep() => ContentPanel.Children.Add(new Separator
    { Background = new SolidColorBrush(Color.FromRgb(37,45,64)), Margin = new Thickness(0,8,0,8) });

    // ── ページ内容 ───────────────────────────────
    private void RenderHome()
    {
        H1("🏠 ホーム画面");
        P("アプリ起動時に最初に表示される画面です。全プロジェクトを横断した状態確認と各機能へのアクセスが行えます。");
        H2("アラートセクション");
        P("登録されているすべてのプロジェクトのタスクから、以下の3種類のアラートを自動収集して表示します。");
        P("🔴 期限超過 — 予定終了日を過ぎてもステータスが「完了」でないタスク");
        P("🟡 締切間近 — 今日から3日以内に予定終了日が来るタスク");
        P("🟠 未着手超過 — ステータスが「未着手」のまま予定開始日を過ぎたタスク");
        Tip("アラート行をクリックすると、該当プロジェクトを自動で開いてタスク一覧に移動します。");
        H2("プロジェクト一覧");
        P("最近使ったプロジェクトがカード形式で表示されます。カードには進捗バー・完了数・対応中数・超過数が表示されます。");
        P("📌ボタンでピン留めするとリストの先頭に固定されます。");
        H2("クイックナビ");
        P("現在開いているプロジェクトの各画面へワンクリックで遷移できます。プロジェクト未選択時はグレーアウトします。");
    }

    private void RenderCreateProject()
    {
        H1("📂 プロジェクト作成");
        H2("新規プロジェクトを作成する");
        Step(1, "左サイドバーの「⚙️ プロジェクト設定」をクリック");
        Step(2, "「作成先フォルダ」の「参照...」で保存場所を選択");
        Step(3, "「プロジェクト名」を入力（例: ECサイト_リニューアル_2025）");
        Step(4, "「プロジェクトを作成」をクリック");
        Tip("作成後、指定フォルダ配下に「プロジェクト名」フォルダが自動生成されます。");
        H2("既存プロジェクトを開く");
        Step(1, "「📂 既存プロジェクトを開く」をクリック");
        Step(2, "プロジェクトフォルダ内の project_data.json を選択");
        H2("Excel からインポート");
        P("WBS_ガントチャート形式の Excel ファイルからタスクを一括インポートできます。");
        Step(1, "先にプロジェクトを新規作成または開く");
        Step(2, "「📊 Excel からインポート」をクリック");
        Step(3, "xlsx ファイルを選択して実行");
        Warn("インポートはタスクを追加します。既存タスクが重複して登録されることがあります。");
    }

    private void RenderMultiProject()
    {
        H1("🔄 複数プロジェクトの管理");
        P("TKer は複数のプロジェクトを登録・切り替えて管理できます。プロジェクトデータは各プロジェクトフォルダ内の project_data.json に独立して保存されます。");
        H2("プロジェクトを切り替える");
        Step(1, "ホーム画面のプロジェクト一覧で切り替えたいカードをクリック");
        Step(2, "自動的にそのプロジェクトが読み込まれ、ダッシュボードに移動");
        Tip("サイドバー下部の「プロジェクトパス」で現在開いているプロジェクトを確認できます。");
        H2("プロジェクトを一覧に追加する");
        P("プロジェクトを「開く」操作を行うと自動的にホームの一覧に追加されます。");
        H2("アラートは全プロジェクト横断");
        P("ホーム画面のアラートセクションは、一覧に登録されているすべてのプロジェクトのタスクを横断して確認します。プロジェクトを開かなくても期限超過を把握できます。");
        H2("ピン留め機能");
        P("よく使うプロジェクトは 📌 ボタンでピン留めすることでリストの先頭に固定できます。");
        Warn("project_data.json が存在しないプロジェクトは一覧から自動的に削除されます。");
    }

    private void RenderCategory()
    {
        H1("📂 カテゴリー管理");
        P("タスクの大分類です。プロジェクト内のフェーズや機能領域をカテゴリーで整理します。");
        H2("カテゴリーを追加する");
        Step(1, "「📂 カテゴリー管理」画面を開く");
        Step(2, "「＋ カテゴリー追加」をクリック");
        Step(3, "名前・説明・カラー（HEX値）を入力して保存");
        Tip("カテゴリー追加と同時に CATXXX_カテゴリー名/ フォルダがプロジェクト配下に自動生成されます。");
        H2("カテゴリーを削除する");
        Warn("カテゴリーを削除すると、配下のタスクもすべて削除されます。フォルダは削除されません。");
    }

    private void RenderTask()
    {
        H1("✅ タスク管理");
        H2("タスクを追加する");
        Step(1, "「✅ タスク一覧」画面を開く");
        Step(2, "「＋ タスク追加」をクリック");
        Step(3, "カテゴリー・タスク名・担当者・優先度・予定期間などを入力");
        Step(4, "「保存」をクリック");
        Tip("タスク名省略形はフォルダ名に使われます。20文字以内を推奨します。");
        H2("ステータス一覧");
        P("未着手 → 対応中 → レビュー中 → 完了 の4段階です。");
        H2("タスクを完了にする");
        P("ステータスを「完了」に変更して保存すると、タスクフォルダごと「作業完了/」フォルダへ自動移動します。");
        Warn("完了後のフォルダ移動は取り消せません。");
        H2("フィルター・検索");
        P("ステータスボタン・カテゴリードロップダウン・検索テキストボックスで絞り込みができます。これらは同時に適用されます。");
    }

    private void RenderFolder()
    {
        H1("📁 フォルダ管理");
        P("タスク起票時に自動生成されるフォルダを使って、成果物や作業ファイルを整理します。");
        H2("フォルダ構成");
        P("プロジェクト/\n  ├── CAT001_カテゴリー名/\n  │   └── TSK0001_タスク名省略形/   ← タスクフォルダ\n  ├── 作業完了/\n  ├── _templates/\n  └── _docs/");
        H2("フォルダを開く");
        P("タスク一覧の 📁 ボタン → エクスプローラーでフォルダを開きます。");
        H2("ファイル一覧を確認する");
        P("タスク一覧の 📄 ボタン → アプリ内でファイル一覧ダイアログが開きます。ファイルをダブルクリックで開くことができます。");
        Tip("プロジェクト画面のフォルダツリーでプロジェクト全体の構成を俯瞰できます。");
    }

    private void RenderGantt()
    {
        H1("📊 ガントチャート");
        P("タスクの予定期間と実績期間をバーチャートで可視化します。");
        H2("見方");
        P("🟦 青バー（上段）: 予定期間\n🟩 緑バー（下段）: 実績期間\n｜シアン縦線: 今日の日付");
        H2("操作");
        P("◀ ▶ で表示月を移動できます。「今日」ボタンで今月に戻ります。");
        P("バーにマウスを乗せると期間がツールチップで表示されます。");
        Tip("青バーと緑バーのズレが遅延を示します。緑が右に飛び出していれば遅延です。");
    }

    private void RenderCalendar()
    {
        H1("📅 カレンダー・スケジュール");
        H2("カレンダー表示");
        P("月カレンダー形式でタスクの予定・実績期間を表示します。セルをホバーするとタスク詳細がツールチップで表示されます。");
        H2("横一覧表示");
        P("「📋 横一覧」ボタンで一覧形式に切り替えられます。予定・実績の日付と遅延日数が数値で確認できます。");
        Tip("遅延日数がマイナスの場合は前倒し完了を意味します。");
    }

    private void RenderDeliverable()
    {
        H1("📦 成果物まとめ");
        P("全タスク（または指定カテゴリー）の完了フォルダ内ファイルを一か所に集約し、ZIPとINDEX.mdを自動生成します。");
        H2("いつ使うか");
        P("プロジェクトの全タスクが完了したあと、各タスクフォルダに散らばった成果物をまとめてクライアントへ納品・アーカイブしたい場合に使います。");
        H2("実行手順");
        Step(1, "左サイドバーの「📦 成果物まとめ」をクリック");
        Step(2, "完了状況サマリーで全タスクが完了していることを確認");
        Step(3, "出力先フォルダを選択（省略するとプロジェクトフォルダ内に生成）");
        Step(4, "オプションを設定して「📦 成果物をまとめる」をクリック");
        H2("出力されるもの");
        P("成果物まとめ_YYYYMMDD_HHMM/\n  ├── カテゴリー名/\n  │   └── タスクID_タスク名/\n  │       └── （コピーされたファイル群）\n  └── INDEX.md（ファイル一覧レポート）\n成果物まとめ_YYYYMMDD_HHMM.zip（ZIPオプション時）");
        H2("オプション説明");
        P("完了タスクのみ: ONのとき完了ステータスのタスクのみ対象（推奨）");
        P("カテゴリー別サブフォルダ: 出力先をカテゴリーごとに整理");
        P("ZIP生成: 集約フォルダを自動ZIP化。納品や送付に便利");
        P("INDEX.md: タスク一覧・ファイル一覧のマークダウンレポートを生成");
        P("拡張子フィルター: .pdf,.docxなど特定ファイルだけ集める場合に指定");
        Warn("「ファイルを移動」オプションを使うとオリジナルが移動します。通常はコピー（デフォルト）を推奨します。");
        Tip("INDEX.mdをそのまま提出書類の目次として活用できます。");
    }

    private void RenderFaq()
    {
        H1("❓ よくある質問");
        H2("Q. プロジェクトファイルはどこに保存されますか？");
        P("プロジェクトフォルダ内の project_data.json に保存されます。バックアップはこのファイルをコピーするだけです。");
        H2("Q. タスクフォルダが作られません");
        P("プロジェクトフォルダのパスに日本語が含まれている場合に問題が起きることがあります。英数字のパスを推奨します。");
        H2("Q. 複数人で同じプロジェクトを使えますか？");
        P("project_data.json を共有フォルダに置くことで複数人で参照できますが、同時編集には対応していません。編集前に保存状態を確認してください。");
        H2("Q. アラートが表示されない");
        P("タスクに予定日が設定されていない場合はアラートが生成されません。タスク編集で予定開始日・終了日を設定してください。");
        Tip("その他のご質問は管理担当者にお問い合わせください。");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
