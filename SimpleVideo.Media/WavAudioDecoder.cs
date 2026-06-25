using System;
using System.IO;
using SimpleVideo.Core.Media;

namespace SimpleVideo.Media;

public class WavAudioDecoder : IAudioDecoder
{
    private readonly FileStream _fileStream;
    private readonly BinaryReader _binaryReader;
    private readonly long _dataStartOffset;
    private readonly int _dataSize;
    
    // Fixed specifications for pre-encoded audio cache
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int BytesPerSample = 2; // 16-bit
    private const int BlockAlign = Channels * BytesPerSample; // 4 bytes
    private const int BytesPerSecond = SampleRate * BlockAlign; // 192,000 bytes

    public WavAudioDecoder(string wavPath)
    {
        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException("WAV file not found.", wavPath);
        }

        _fileStream = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _binaryReader = new BinaryReader(_fileStream);

        // Parse WAV chunks to locate "data" chunk
        _dataStartOffset = FindDataChunk(out _dataSize);
    }

    private long FindDataChunk(out int dataSize)
    {
        _fileStream.Position = 0;
        
        string riff = new string(_binaryReader.ReadChars(4));
        if (riff != "RIFF") throw new InvalidDataException("Not a valid RIFF/WAV file.");
        
        _binaryReader.ReadInt32(); // Chunk size
        
        string wave = new string(_binaryReader.ReadChars(4));
        if (wave != "WAVE") throw new InvalidDataException("Not a valid WAVE format.");

        while (_fileStream.Position < _fileStream.Length)
        {
            if (_fileStream.Position + 8 > _fileStream.Length)
            {
                break;
            }

            string chunkId = new string(_binaryReader.ReadChars(4));
            int chunkSize = _binaryReader.ReadInt32();

            if (chunkId == "data")
            {
                dataSize = chunkSize;
                return _fileStream.Position;
            }
            
            // Skip the chunk data, keeping alignment (chunkSize can be odd, but in wav mostly even)
            long nextPos = _fileStream.Position + chunkSize;
            if (nextPos > _fileStream.Length)
            {
                break;
            }
            _fileStream.Position = nextPos;
        }

        throw new InvalidDataException("WAV file does not contain a data chunk.");
    }

    public AudioBuffer GetAudio(TimeSpan start, TimeSpan duration)
    {
        // Calculate byte positions
        long startByteOffset = (long)(start.TotalSeconds * BytesPerSecond);
        long durationBytes = (long)(duration.TotalSeconds * BytesPerSecond);

        // Align offsets to BlockAlign (4 bytes)
        startByteOffset = (startByteOffset / BlockAlign) * BlockAlign;
        durationBytes = (durationBytes / BlockAlign) * BlockAlign;

        if (startByteOffset < 0) startByteOffset = 0;
        
        // Clamp to data bounds
        if (startByteOffset >= _dataSize)
        {
            return new AudioBuffer(Array.Empty<short>(), SampleRate, Channels);
        }

        if (startByteOffset + durationBytes > _dataSize)
        {
            durationBytes = _dataSize - startByteOffset;
        }

        if (durationBytes <= 0)
        {
            return new AudioBuffer(Array.Empty<short>(), SampleRate, Channels);
        }

        short[] samples = new short[durationBytes / BytesPerSample];
        
        lock (_fileStream)
        {
            _fileStream.Position = _dataStartOffset + startByteOffset;
            
            // Read bytes and convert to shorts
            byte[] buffer = _binaryReader.ReadBytes((int)durationBytes);
            Buffer.BlockCopy(buffer, 0, samples, 0, buffer.Length);
        }

        return new AudioBuffer(samples, SampleRate, Channels);
    }

    public void Dispose()
    {
        _binaryReader?.Dispose();
        _fileStream?.Dispose();
    }
}
