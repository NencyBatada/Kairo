import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { initials } from "@/lib/initials";
import { cn } from "@/lib/utils";

type UserAvatarProps = {
  name: string;
  pictureUrl?: string | null;
  isYou?: boolean;
  className?: string;
};

export function UserAvatar({ name, pictureUrl, isYou = false, className }: UserAvatarProps) {
  return (
    <Avatar aria-hidden="true" className={cn(isYou && "bg-primary-soft", className)}>
      {pictureUrl && <AvatarImage src={pictureUrl} alt="" referrerPolicy="no-referrer" />}
      <AvatarFallback className={cn(isYou && "text-primary-strong")}>{initials(name)}</AvatarFallback>
    </Avatar>
  );
}
