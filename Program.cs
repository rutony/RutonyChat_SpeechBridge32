using System;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.Wave;

class Program {

    static void Main(string[] args) {

        bool debugMode = args.Contains("--debug") || args.Contains("-db");

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) {
            ShowHelp();
            return;
        }

        if (args.Contains("--list-voices") || args.Contains("-lv")) {
            ListVoices();
            return;
        }

        if (args.Contains("--list-devices") || args.Contains("-ld")) {
            ListDevices();
            return;
        }

        string? voice = null;
        string? text = null;
        int rate = 0;
        int volume = 100;
        string outputDeviceId = "0";
        string outputFile = null;
        bool randomVoice = args.Contains("--random-voice") || args.Contains("-rv");
        bool randomRate = args.Contains("--random-rate") || args.Contains("-rr");
        int chunkSize = 50; // Значение по умолчанию для размера чанка

        // Парсинг аргументов командной строки
        for (int i = 0; i < args.Length; i++) {
            switch (args[i]) {
                case "--voice":
                case "-v":
                    voice = args[++i];
                    break;
                case "--text":
                case "-t":
                    text = string.Join(" ", args.Skip(i + 1));
                    i = args.Length; // Завершаем цикл
                    break;
                case "--rate":
                case "-r":
                    rate = int.Parse(args[++i]);
                    rate = Math.Clamp(rate, -10, 10);
                    break;
                case "--volume":
                case "-vol":
                    volume = int.Parse(args[++i]);
                    volume = Math.Clamp(volume, 0, 100);
                    break;
                case "--device":
                case "-d":
                    outputDeviceId = args[++i];
                    break;
                case "--output":
                case "-o":
                    outputFile = args[++i];
                    break;
                case "--chunk-size":
                case "-cs":
                    chunkSize = int.Parse(args[++i]);
                    chunkSize = Math.Max(1, chunkSize); // Минимальный размер чанка - 1
                    break;
            }
        }

        // Если выбран случайный голос
        if (randomVoice) {
            var voices = new SpeechSynthesizer().GetInstalledVoices();
            if (voices.Count > 0) {
                var random = new Random();
                voice = voices[random.Next(voices.Count)].VoiceInfo.Name;
                Console.WriteLine($"Выбран случайный голос: {voice}");
            } else {
                Console.WriteLine("Нет доступных голосов.");
                return;
            }
        }

        // Если выбрана случайная скорость
        if (randomRate) {
            var random = new Random();
            rate = random.Next(-10, 11); // Случайное значение от -10 до 10
            Console.WriteLine($"Выбрана случайная скорость: {rate}");
        }

        // Отладочная информация о параметрах
        if (debugMode) {
            Console.WriteLine("[DEBUG] Параметры синтеза:");
            Console.WriteLine($"Голос: {voice ?? "по умолчанию"}");
            Console.WriteLine($"Скорость: {rate}");
            Console.WriteLine($"Громкость: {volume}");
            Console.WriteLine($"Устройство вывода: {outputDeviceId}");
            Console.WriteLine($"Размер чанка: {chunkSize}");
            Console.WriteLine("----------------------");

            // Вывод информации о версии, дате и времени сборки
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var buildDate = System.IO.File.GetLastWriteTime(System.Reflection.Assembly.GetExecutingAssembly().Location);
            Console.WriteLine($"[DEBUG] Версия программы: {version}");
            Console.WriteLine($"[DEBUG] Дата и время сборки: {buildDate}");
            Console.WriteLine("----------------------");
        }

        // Если текст не указан, завершаем выполнение
        if (string.IsNullOrEmpty(text)) {
            Console.WriteLine("Текст для озвучивания не указан.");
            return;
        }

        // Инициализация синтезатора речи
        using (var synthesizer = new SpeechSynthesizer()) {
            // Выбор голоса (оставить без изменений)
            if (voice != null) {
                var voiceInfo = synthesizer.GetInstalledVoices()
                    .FirstOrDefault(v => v.VoiceInfo.Name.Contains(voice, StringComparison.OrdinalIgnoreCase));
                if (voiceInfo != null) {
                    synthesizer.SelectVoice(voiceInfo.VoiceInfo.Name);
                } else {
                    Console.WriteLine($"Голос '{voice}' не найден.");
                    return;
                }
            }

            // Настройка скорости и громкости (оставить без изменений)
            synthesizer.Rate = rate;
            synthesizer.Volume = volume;

            // Разделение текста на чанки
            var chunks = SplitTextIntoChunks(text, chunkSize, debugMode);
            if (chunks.Count == 0) {
                Console.WriteLine("Не удалось разделить текст на чанки.");
                return;
            }

            // Получение формата аудио из первого чанка
            WaveFormat waveFormat;
            using (var tempStream = new MemoryStream()) {
                synthesizer.SetOutputToWaveStream(tempStream);
                synthesizer.Speak(chunks[0]);
                tempStream.Position = 0;
                using var reader = new WaveFileReader(tempStream);
                waveFormat = reader.WaveFormat;
            }

            // Синтез аудио из всех чанков
            byte[] audioData = SynthesizeChunksToAudio(synthesizer, chunks, waveFormat);

            // Сохранение в файл или воспроизведение
            if (!string.IsNullOrEmpty(outputFile)) {
                File.WriteAllBytes(outputFile, audioData);
                Console.WriteLine($"Аудио сохранено в файл: {outputFile}");
            } else {
                using var finalStream = new MemoryStream(audioData);
                PlayAudioFromMemory(finalStream, outputDeviceId ?? "0");
            }
        }

        static List<string> SplitTextIntoChunks(string text, int chunkSize, bool debugMode) {
            var chunks = new List<string>();
            text = Regex.Replace(text, @"\s+", " ").Trim();

            // Если текст состоит из одной длинной строки без пробелов
            if (!text.Contains(" ")) {
                // Разбиваем текст на чанки фиксированного размера
                for (int i = 0; i < text.Length; i += chunkSize) {
                    int length = Math.Min(chunkSize, text.Length - i);
                    string chunk = text.Substring(i, length);
                    chunks.Add(chunk);

                    if (debugMode) {
                        Console.WriteLine($"[DEBUG] Чанк #{chunks.Count}");
                        Console.WriteLine($"Длина: {chunk.Length} символов");
                        Console.WriteLine($"Содержимое: \"{chunk}\"");
                        Console.WriteLine("----------------------");
                    }
                }
                return chunks;
            }

            // Оригинальная логика для текста с пробелами
            var tokens = Regex.Matches(text, @"(\d+|\s+|[^\s\w]|\w+)")
                .Cast<Match>()
                .Select(m => m.Value)
                .ToList();

            int index = 0;
            while (index < tokens.Count) {
                var chunk = new StringBuilder();
                int currentLength = 0;
                bool isLastChunk = false;

                while (index < tokens.Count && currentLength < chunkSize) {
                    string token = tokens[index];
                    bool isSpace = string.IsNullOrWhiteSpace(token);
                    bool isNumber = Regex.IsMatch(token, @"^\d+$");
                    bool isPunctuation = Regex.IsMatch(token, @"^[^\s\w]$");

                    // Пропускаем пробелы в начале чанка
                    if (chunk.Length == 0 && isSpace) {
                        index++;
                        continue;
                    }

                    // Обработка чисел
                    if (isNumber && token.Length > 8) {
                        var numberParts = SplitNumber(token, 8);
                        foreach (var part in numberParts) {
                            if (currentLength + part.Length > chunkSize)
                                break;
                            chunk.Append(part);
                            currentLength += part.Length;
                        }
                        index++;
                        continue;
                    }

                    // Обработка длинных токенов
                    if (!isSpace && token.Length > chunkSize) {
                        var parts = SplitLongToken(token, chunkSize);
                        foreach (var part in parts) {
                            if (currentLength + part.Length > chunkSize)
                                break;
                            chunk.Append(part);
                            currentLength += part.Length;
                        }
                        index++;
                        continue;
                    }

                    // Проверка на переполнение чанка
                    if (currentLength + token.Length > chunkSize)
                        break;

                    // Добавляем токен (сохраняем пробелы)
                    chunk.Append(token);
                    currentLength += token.Length;
                    index++;
                }

                // Проверка окончания чанка
                isLastChunk = (index >= tokens.Count);
                if (!isLastChunk) {
                    string chunkStr = chunk.ToString();
                    while (chunkStr.Length > 0 && !IsValidChunkEnd(chunkStr)) {
                        index--;
                        chunkStr = chunkStr.Substring(0, chunkStr.Length - tokens[index].Length);
                        currentLength -= tokens[index].Length;
                    }
                    chunk.Clear().Append(chunkStr);
                }

                if (chunk.Length > 0) {
                    // Убираем пробелы в конце чанка
                    string finalChunk = Regex.Replace(chunk.ToString().TrimEnd(), @"\s+", " ");
                    chunks.Add(finalChunk);

                    if (debugMode) {
                        Console.WriteLine($"[DEBUG] Чанк #{chunks.Count}");
                        Console.WriteLine($"Длина: {finalChunk.Length} символов");
                        Console.WriteLine($"Содержимое: \"{finalChunk}\"");
                        Console.WriteLine("----------------------");
                    }
                }
            }

            return chunks;
        }

        // Вспомогательные методы для разбиения
        static List<string> SplitNumber(string number, int maxDigits) {
            var parts = new List<string>();
            for (int i = 0; i < number.Length; i += maxDigits) {
                int length = Math.Min(maxDigits, number.Length - i);
                parts.Add(number.Substring(i, length));
            }
            return parts;
        }

        static List<string> SplitLongToken(string token, int chunkSize) {
            var parts = new List<string>();
            for (int i = 0; i < token.Length; i += chunkSize) {
                int length = Math.Min(chunkSize, token.Length - i);
                parts.Add(token.Substring(i, length));
            }
            return parts;
        }

        static bool IsValidChunkEnd(string chunk) {
            if (chunk.Length == 0)
                return true;

            // Проверка на пунктуацию в конце
            if (Regex.IsMatch(chunk[^1].ToString(), @"[^\s\w]"))
                return true;

            // Разбиваем последнее "слово"
            var lastWord = Regex.Match(chunk, @"(\d+|\w+|[^\s\w])$").Value;
            return lastWord.Length >= 4 || string.IsNullOrWhiteSpace(lastWord);
        }

        static byte[] SynthesizeChunksToAudio(SpeechSynthesizer synthesizer, List<string> chunks, WaveFormat waveFormat) {
            using var mergedStream = new MemoryStream();
            using (var writer = new WaveFileWriter(mergedStream, waveFormat)) {
                foreach (var chunk in chunks) {
                    using var tempStream = new MemoryStream();
                    synthesizer.SetOutputToWaveStream(tempStream);
                    synthesizer.Speak(chunk);
                    tempStream.Position = 0;

                    using (var reader = new WaveFileReader(tempStream)) {
                        var buffer = new byte[tempStream.Length];
                        tempStream.Read(buffer, 0, buffer.Length);
                        writer.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            return mergedStream.ToArray();
        }

        /// <summary>
        /// Воспроизведение аудио из MemoryStream через NAudio
        /// </summary>
        /// <param name="memoryStream">Поток с аудиоданными</param>
        /// <param name="deviceId">ID устройства вывода (если null, используется устройство по умолчанию)</param>
        static void PlayAudioFromMemory(MemoryStream memoryStream, string deviceId) {
            memoryStream.Position = 0;

            using (var waveStream = new WaveFileReader(memoryStream))
            using (var waveOut = new WaveOutEvent()) {
                if (deviceId != null) {
                    if (int.TryParse(deviceId, out int deviceNumber)) {
                        if (deviceNumber >= 0 && deviceNumber < WaveOut.DeviceCount) {
                            waveOut.DeviceNumber = deviceNumber;
                        } else {
                            Console.WriteLine($"Устройство с ID '{deviceId}' не найдено. Используется устройство по умолчанию.");
                        }
                    } else {
                        Console.WriteLine($"Некорректный ID устройства: '{deviceId}'. Используется устройство по умолчанию.");
                    }
                }

                waveOut.Init(waveStream);
                waveOut.Play();

                // Ожидание завершения воспроизведения
                while (waveOut.PlaybackState == PlaybackState.Playing) {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Вывод справки
        /// </summary>
        static void ShowHelp() {
            Console.WriteLine("Озвучивание текста для программы RutonyChat (c) rutony / 2025");
            Console.WriteLine();
            Console.WriteLine("Использование: RutonyChat_SpeechBridge32 [параметры]");
            Console.WriteLine("Параметры:");
            Console.WriteLine("  --voice, -v <голос>            Указать голос для озвучивания");
            Console.WriteLine("  --text, -t <текст>             Указать текст для озвучивания");
            Console.WriteLine("  --rate, -r <скорость>          Указать скорость озвучивания (от -10 до 10)");
            Console.WriteLine("  --volume, -vol <громкость>     Указать громкость (от 0 до 100)");
            Console.WriteLine("  --device, -d <ID>              Указать устройство вывода звука (по умолчанию используется системное)");
            Console.WriteLine("  --output, -o <имя файла>       Сохранить аудио в файл (без воспроизведения)");
            Console.WriteLine("  --random-voice, -rv            Использовать случайный голос");
            Console.WriteLine("  --random-rate, -rr             Использовать случайную скорость");
            Console.WriteLine("  --chunk-size, -cs <размер>     Указать размер чанка (по умолчанию 50)");
            Console.WriteLine("  --list-voices, -lv             Вывести список доступных голосов");
            Console.WriteLine("  --list-devices, -ld            Вывести список доступных устройств");
            Console.WriteLine("  --debug, -db                   Выводить отладочную информацию");
            Console.WriteLine("  --help, -h                     Показать справку");
            Console.WriteLine("");

            Console.WriteLine("Примеры:");
            Console.WriteLine("RutonyChat_SpeechBridge32.exe -v \"Microsoft Irina Desktop\" -t \"Привет, мир!\" -o \"output.wav\"");
            Console.WriteLine("RutonyChat_SpeechBridge32.exe --voice \"Microsoft Irina Desktop\" --text \"Привет, мир!\" --output \"output.wav\"");
            Console.WriteLine("RutonyChat_SpeechBridge32.exe --chunk-size 100 -t \"Длинный текст для синтеза речи\"");
            Console.WriteLine("  * некоторые голоса озвучивания не справляются с слишком большим текстом, поэтому она разбивается на чанки");
            Console.WriteLine("RutonyChat_SpeechBridge32.exe -v irina -t Привет, мир!");
            Console.WriteLine("  * в случае остутствия ковычек, текстом считается все что идет за параметром текст");
        }

        /// <summary>
        /// Вывод списка доступных голосов с информацией о поле и языке
        /// </summary>
        static void ListVoices() {
            using (var synthesizer = new SpeechSynthesizer()) {
                foreach (var voice in synthesizer.GetInstalledVoices()) {
                    var voiceInfo = voice.VoiceInfo;
                    string gender = voiceInfo.Gender == VoiceGender.Male ? "Мужской" :
                                    voiceInfo.Gender == VoiceGender.Female ? "Женский" :
                                    "Неизвестно";
                    string language = voiceInfo.Culture.DisplayName;

                    Console.WriteLine($"{voiceInfo.Name} -- {gender}, {language}");
                }
            }
        }

        /// <summary>
        /// Вывод списка доступных устройств вывода звука
        /// </summary>
        static void ListDevices() {
            for (int i = 0; i < WaveOut.DeviceCount; i++) {
                var capabilities = WaveOut.GetCapabilities(i);
                Console.WriteLine($"{i} -- {capabilities.ProductName}");
            }
        }
    }
}