const graphemes = new Intl.Segmenter(undefined, { granularity: "grapheme" });

function firstGrapheme(word: string) {
  const first = graphemes.segment(word)[Symbol.iterator]().next();
  return first.done ? "" : first.value.segment;
}

export function initials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return "?";
  }

  const letters = words.length === 1 ? firstGrapheme(words[0]) : firstGrapheme(words[0]) + firstGrapheme(words[words.length - 1]);
  return letters.toLocaleUpperCase();
}
