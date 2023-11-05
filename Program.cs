using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using static GenshinDataScraper.Program;

namespace GenshinDataScraper
{
    internal class Program
    {
        const string dbName = "genshinpray";//genshinpray_v2
        const string sqlFilePath = @"Data\initData.sql";
        static async Task Main(string[] args)
        {
            string[] existingSqlLines = File.ReadAllLines(sqlFilePath);
            HashSet<string> existingNames = new HashSet<string>(
                existingSqlLines
                    .Select(line => Regex.Match(line, @"`GoodsName`, `GoodsType`, `GoodsSubType`, `RareType`, `IsPerm`, `CreateDate`, `IsDisable`\) VALUES \('.*?', '(.*?)'", RegexOptions.IgnoreCase))
                    .Where(match => match.Success)
                    .Select(match => match.Groups[1].Value)
            );
            int maxId = existingSqlLines
                .Select(line => Regex.Match(line, @"VALUES \('(.*?)'", RegexOptions.IgnoreCase))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value))
                .Max();
            Console.WriteLine($"已经读取{existingNames.Count}个项目，当前最大Id为{maxId}");
            List<string> newSqlLines = new List<string>();

            string CharacterUrl = "https://genshin.honeyhunterworld.com/fam_chars/?lang=CHS";
            Directory.CreateDirectory(@"Data\角色大图");
            Directory.CreateDirectory(@"Data\角色小图");
            List<Character> characters = GetCharacters(await GetPageContentAsync(CharacterUrl));
            Console.WriteLine($"共获取到{characters.Count}个角色");
            foreach (Character character in characters)
            {
                if (!existingNames.Contains(character.Name))
                {
                    string newSqlLine = GenerateSqlLine(character, ++maxId);  // 根据你的Character对象和新ID生成一个SQL插入语句
                    newSqlLines.Add(newSqlLine);
                    DownloadCharacterImages(character);
                    Console.WriteLine($"处理完成：{character.Name}");
                }
            }

            List<(string Url, SubType Type)> weaponUrls = new()
            {
                ("https://genshin.honeyhunterworld.com/fam_sword/?lang=CHS", SubType.Sword),
                ("https://genshin.honeyhunterworld.com/fam_claymore/?lang=CHS", SubType.Claymore),
                ("https://genshin.honeyhunterworld.com/fam_polearm/?lang=CHS", SubType.Polearm),
                ("https://genshin.honeyhunterworld.com/fam_catalyst/?lang=CHS", SubType.Catalyst),
                ("https://genshin.honeyhunterworld.com/fam_bow/?lang=CHS", SubType.Bow),
            };
            Directory.CreateDirectory(@"Data\武器");
            foreach (var (url, type) in weaponUrls)
            {
                List<Weapon> weapons = GetWeapons(await GetPageContentAsync(url), type);
                Console.WriteLine($"共获取到{weapons.Count}个{type}类武器");
                foreach (Weapon weapon in weapons)
                {
                    if (!existingNames.Contains(weapon.Name))
                    {
                        string newSqlLine = GenerateSqlLine(weapon, ++maxId);  // 根据你的Weapon对象和新ID生成一个SQL插入语句
                        newSqlLines.Add(newSqlLine);
                        DownloadWeaponImages(weapon);
                        Console.WriteLine($"处理完成：{weapon.Type} - {weapon.Name}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(existingSqlLines.LastOrDefault()))
                File.AppendAllText(sqlFilePath, Environment.NewLine);
            File.AppendAllLines(sqlFilePath, newSqlLines);
            Console.WriteLine($"全部处理完成！");
        }

        private static async Task<string> GetPageContentAsync(string url)
        {
            using HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        #region Character
        private static List<Character> GetCharacters(string content)
        {
            // 提取 sortable_data.push 开头的内容
            string sortable_data = Regex.Match(content, @"sortable_data\.push\(\[(.*?)\]\);", RegexOptions.Singleline).Groups[1].Value;
            MatchCollection matches = Regex.Matches(sortable_data, @"\[""<a href=\\""\\\/(.*?)\\\/\?lang=CHS\\"".*?alt=\\""([^""]+)\\"".*?class=\\""rsh\\"">(\d+).*?class=\\""rsh\\"">(\w+).*?class=\\""rsh\\"">(\w+).*?\]");
            List<Character> characters = new();
            foreach (Match match in matches)
            {
                string internalId = match.Groups[1].Value;
                string name = Regex.Unescape(match.Groups[2].Value);
                string rarity = match.Groups[3].Value;
                string weapon = match.Groups[4].Value;
                string element = match.Groups[5].Value;

                if (name != "旅行者")
                    characters.Add(new Character(internalId, name, rarity, weapon, element));
            }
            return characters;
        }


        static void DownloadCharacterImages(Character character)
        {
            _ = ProcessLargeImage(character);
            _ = ProcessSmallImage(character);
        }
        static async Task ProcessLargeImage(Character character)
        {
            using HttpClient httpClient = new HttpClient();
            string splashUrl = $"https://genshin.honeyhunterworld.com/img/{character.InternalId}_gacha_splash.webp";
            // 读取原始图像
            using (Image<Rgba32> image = Image.Load<Rgba32>(await httpClient.GetByteArrayAsync(splashUrl)))
            {
                // 计算新的尺寸，保持原有的宽高比
                float scale = Math.Min(2048f / image.Width, 1024f / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);

                // 创建一个新的透明背景的画布
                using (Image<Rgba32> canvas = new Image<Rgba32>(2048, 1024))
                {
                    // 计算图像应该被绘制的位置，以便它位于画布的中心
                    int x = (2048 - newWidth) / 2;
                    int y = (1024 - newHeight) / 2;

                    // 将原始图像绘制到画布上
                    canvas.Mutate(ctx => ctx
                        .DrawImage(image.Clone(img => img
                            .Resize(new ResizeOptions
                            {
                                Size = new Size(newWidth, newHeight),
                                Mode = ResizeMode.Max
                            })), new Point(x, y), 1));

                    // 保存处理后的图像为PNG格式
                    canvas.Save($@"Data\角色大图\{character.Name}.png");
                }
            }
        }
        static async Task ProcessSmallImage(Character character)
        {
            using HttpClient httpClient = new HttpClient();
            string cardUrl = $"https://genshin.honeyhunterworld.com/img/{character.InternalId}_gacha_card.webp";
            // 读取原始图像
            using (Image<Rgba32> image = Image.Load<Rgba32>(await httpClient.GetByteArrayAsync(cardUrl)))

            using (Image<Rgba32> mask = Image.Load<Rgba32>(@"Data\钟离.png")) // 加载遮罩图像，岩王爷罩我！
            {
                // 计算缩放比例，以使图像尽可能填充 149x606 的区域，同时保持纵横比
                float scale = Math.Max(149f / image.Width, 606f / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);

                // 计算裁剪区域，以使缩放后的图像居中
                int x = (newWidth - 149) / 2;
                int y = (newHeight - 606) / 2;
                var cropRect = new Rectangle(x, y, 149, 606);

                image.Mutate(ctx => ctx
                    // 先将图像缩放到新尺寸
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(newWidth, newHeight),
                        Mode = ResizeMode.BoxPad
                    })
                    // 然后裁剪图像以适应 149x606 的区域
                    .Crop(cropRect));
                using (Image<Rgba32> canvas = new Image<Rgba32>(149, 606))
                {
                    canvas.Mutate(ctx => ctx.DrawImage(image, new Point(0, 0), 1));

                    // 应用遮罩
                    for (int j = 0; j < canvas.Height; j++)
                    {
                        for (int i = 0; i < canvas.Width; i++)
                        {
                            Rgba32 pixelColor = canvas[i, j];
                            Rgba32 maskColor = mask[i, j];

                            if (maskColor.A == 0)
                                pixelColor.A = 0;
                            canvas[i, j] = pixelColor;
                        }
                    }

                    // 保存处理后的图像为PNG格式
                    canvas.Save($@"Data\角色小图\{character.Name}.png");
                }
            }
        }
        static string GenerateSqlLine(Character character, int id)
        {
            // 根据你的Character对象和新ID生成一个SQL插入语句
            string createDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return $"INSERT INTO `{dbName}`.`goods` (`Id`, `GoodsName`, `GoodsType`, `GoodsSubType`, `RareType`, `IsPerm`, `CreateDate`, `IsDisable`) VALUES ('{id}', '{character.Name}', '1', '{(int)character.Element}', '{character.Rarity}', '0', '{createDate}', '0');";
        }
        #endregion
        #region Weapon
        private static List<Weapon> GetWeapons(string content, SubType type)
        {
            // 提取 sortable_data.push 开头的内容
            string sortable_data = Regex.Match(content, @"sortable_data\.push\(\[(.*?)\]\);", RegexOptions.Singleline).Groups[1].Value;
            MatchCollection matches = Regex.Matches(sortable_data, @"\[""<a href=\\""\\\/(.*?)\\\/\?lang=CHS\\"".*?alt=\\""([^""]+)\\"".*?class=\\""rsh\\"">(\d+).*?\]");
            List<Weapon> weapons = new();
            foreach (Match match in matches)
            {
                string internalId = match.Groups[1].Value;
                string name = Regex.Unescape(match.Groups[2].Value);
                string rarity = match.Groups[3].Value;

                if (rarity != "1" && rarity != "2" && !match.Value.Contains(@"\/i_2001\/?lang=CHS")) //排除1星和2星武器，排除所有用摩拉升级的武器（这些武器不会出现在祈愿中）
                    weapons.Add(new Weapon(internalId, name, rarity, type));
            }
            return weapons;
        }
        static void DownloadWeaponImages(Weapon weapon)
        {
            _ = ProcessIconImage(weapon);
        }
        public static async Task ProcessIconImage(Weapon weapon)
        {
            using HttpClient httpClient = new HttpClient();
            string iconUrl = $"https://genshin.honeyhunterworld.com/img/{weapon.InternalId}_gacha_icon.webp";
            using Image<Rgba32> image = Image.Load<Rgba32>(await httpClient.GetByteArrayAsync(iconUrl)); // 加载源图像
            int canvasWidth = 512;
            int canvasHeight = 1024;

            // 创建一个透明的画布
            using Image<Rgba32> canvas = new Image<Rgba32>(canvasWidth, canvasHeight);

            // 计算图像应该放置的位置以便于它位于画布的中心
            int xPosition = (canvasWidth - image.Width) / 2;
            int yPosition = (canvasHeight - image.Height) / 2;

            // 创建阴影效果
            using (Image<Rgba32> shadow = image.Clone())
            {
                // 创建阴影效果
                shadow.ProcessPixelRows(accessor =>
                {
                    // Color is pixel-agnostic, but it's implicitly convertible to the Rgba32 pixel type
                    Rgba32 transparent = Color.Transparent;

                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                        // pixelRow.Length has the same value as accessor.Width,
                        // but using pixelRow.Length allows the JIT to optimize away bounds checks:
                        for (int x = 0; x < pixelRow.Length; x++)
                        {
                            // Get a reference to the pixel at position x
                            ref Rgba32 pixel = ref pixelRow[x];
                            if (pixel.A != 0)
                            {
                                pixelRow[x] = new Rgba32(0, 0, 0, pixel.A);
                            }
                        }
                    }
                });
                // 模糊阴影
                shadow.Mutate(ctx => ctx.GaussianBlur(2.5f));
                // 在画布上绘制阴影，偏移一些像素
                canvas.Mutate(ctx => ctx.DrawImage(shadow, new Point(xPosition + 3, yPosition + 12), 1f));
            }

            // 在画布上绘制原始图像
            canvas.Mutate(ctx => ctx.DrawImage(image, new Point(xPosition-5, yPosition-9), 1f));

            // 将结果保存为PNG文件
            canvas.Save($@"Data\武器\{weapon.Name}.png");
        }
        static string GenerateSqlLine(Weapon weapon, int id)
        {
            // 根据你的Character对象和新ID生成一个SQL插入语句
            string createDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return $"INSERT INTO `{dbName}`.`goods` (`Id`, `GoodsName`, `GoodsType`, `GoodsSubType`, `RareType`, `IsPerm`, `CreateDate`, `IsDisable`) VALUES ('{id}', '{weapon.Name}', '2', '{(int)weapon.Type}', '{weapon.Rarity}', '0', '{createDate}', '0');";
        }
        #endregion


        #region Structure
        public class Character
        {
            public string InternalId { get; set; }
            public string Name { get; set; }
            public string Rarity { get; set; }
            public SubType Weapon { get; set; }
            public SubType Element { get; set; }
            //构造函数
            public Character(string internalId, string name, string rarity, string weapon, string element)
            {
                InternalId = internalId;
                Name = name;
                Rarity = rarity;
                Weapon = (SubType)Enum.Parse(typeof(SubType), weapon, true);
                Element = (SubType)Enum.Parse(typeof(SubType), element, true);
            }
        }
        public class Weapon
        {
            public string InternalId { get; set; }
            public string Name { get; set; }
            public string Rarity { get; set; }
            public SubType Type { get; set; }
            //构造函数
            public Weapon(string internalId, string name, string rarity, SubType type)
            {
                InternalId = internalId;
                Name = name;
                Rarity = rarity;
                Type = type;
            }
        }
        public enum SubType
        {
            其他 = 0,//其他

            Pyro = 1,//火
            Hydro = 2,//水
            Anemo = 3,//风
            Electro = 4,//雷
            Dendro = 5,//草
            Cryo = 6,//冰
            Geo = 7,//岩

            Sword = 8,//单手剑
            Claymore = 9,//双手剑
            Polearm = 10,//长柄武器
            Catalyst = 11,//法器
            Bow = 12//弓
        } 
        #endregion
    }
}