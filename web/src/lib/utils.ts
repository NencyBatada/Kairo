import { createCn } from "cn/config";

export const cn = createCn({
  extend: {
    classGroups: {
      "font-size": [{ text: ["display-xl", "display-lg", "display-md", "display-sm", "small", "eyebrow"] }],
    },
  },
});
