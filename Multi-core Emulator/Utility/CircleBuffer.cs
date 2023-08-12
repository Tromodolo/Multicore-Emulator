namespace MultiCoreEmulator.Utility; 

public class CircleBuffer<T> {
	int start;
	int end;

	T[] data;

	public CircleBuffer(uint size) {
		data = new T[size];
		start = 0;
		end = 0;
	}

	public void AddBack(T value) {
		data[end] = value;
		
		end += 1;
		end %= data.Length;

		if (end == start) {
			// Console.WriteLine("Audio Buffer Overflowing");
			
			start += 1;
			start %= data.Length;
		}
	}

	public T RemoveFront() {
		if (start == end) {
			throw new OverflowException("");
		}

		var value = data[start];

		start += 1;
		start %= data.Length;
		
		return value;
	}

	public void Clear() {
		start = 0;
		end = 0;
	}

	public T[] FillBuffer(ref T[] buffer) {
		for (int i = 0; i < buffer.Length; i++) {
			if (start == end) {
				// Console.WriteLine("Audio Buffer too empty");
				break;
			}
			
			var value = data[start];

			start += 1;
			start %= data.Length;

			buffer[i] = value;
		}

		return buffer;
	}
}
